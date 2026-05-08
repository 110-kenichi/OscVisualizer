using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using DynamicData;
using MathNet.Numerics;
using MathNet.Numerics.Distributions;
using MathNet.Numerics.IntegralTransforms;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using OscVisualizer.Models;
using OscVisualizer.ViewModels;
using OscVisualizer.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OscVisualizer.Services
{
    /// <summary>
    /// Processing風スクリプトで線分を生成するオーディオビジュアライザ。
    /// </summary>
    /// <remarks>
    /// 仕様:
    /// - 入力: ProcessingViewModel.Text に記述したスクリプトを実行する。
    /// - 出力: 描画関数 1 回につき XYPoint の線分列を出力する。
    /// - 文: 改行または ';' 区切り。
    ///   - 行末コメント: // ... / # ... をサポート
    ///   - 改行継続: 次行が演算子などで始まる場合は同一式として継続
    /// - 代入文: 変数 = 式
    /// - for 文:
    ///   - for(init; condition; step) statement
    ///   - for(init; condition; step) { ... }
    /// - 描画文:
    ///   - line(x1, y1, x2, y2[, intensity])
    ///   - point(x, y[, size][, intensity]) ※非常に短い線分で描画
    ///   - rect(x, y, w, h[, intensity])
    ///   - ellipse(x, y, w, h[, intensity])
    ///   - circle(x, y, d, s[, intensity]) ※d: 直径, s: 分割数
    ///   - triangle(x1, y1, x2, y2, x3, y3[, intensity])
    ///   - arc(x, y, w, h, start, stop, split[, intensity]) ※扇形, split: 分割数
    /// - 演算子:
    ///   - 算術: +, -, *, /, %, ^（べき乗）
    ///   - 比較: <, <=, >, >=, ==, !=
    ///   - 条件: ?:
    ///   - インクリメント/デクリメント: ++, --（前置/後置）
    /// - 単項演算子: +, -
    /// - 関数: sin, cos, tan, asin, acos, atan, atan2, sqrt, pow,
    ///         abs, min, max, floor, ceil, round, clamp, mag
    ///   - mag(x, y) / mag(x, y, z)
    /// - システム変数: kick, snare, hat, time, pi, tau
    /// - 真偽値評価: 0 は偽、それ以外は真
    /// - 未定義変数: 0 として評価
    /// - 安全対策: for ループは最大反復回数で打ち切り
    /// - パース/実行失敗時: そのフレームは空の線分リストを返す
    ///
    /// 使用例:
    /// for(i = 0; i < 120; i++)
    /// {
    ///     a = i / 120 * tau;
    ///     point(cos(a) * 0.8, sin(a) * 0.8, 0.003, 0.8);
    /// }
    /// rect(-0.5, -0.5, 1.0, 1.0, 0.6);
    /// arc(0, 0, 1.0, 1.0, 0, pi, 32, 1.0);
    /// </remarks>
    internal class Processing : IAudioVisualizer
    {

        public string VisualizerName
        {
            get => "OscProcessing";
        }

        private UserControl? _visualizerView;

        /// <summary>
        /// 
        /// </summary>
        public UserControl? VisualizerView
        {
            get
            {
                return _visualizerView;
            }
        }

        private readonly Stopwatch _sw = Stopwatch.StartNew();

        private readonly ProcessingViewModel settingsViewModel = new ProcessingViewModel();

        private ScriptProgram? _compiledProgram;
        private string? _compiledSource;

        /// <summary>
        /// Initializes a new instance of the Processing class.
        /// </summary>
        /// <remarks>This constructor sets up the visualizer view for the Processing instance. Use this
        /// constructor when you need to create a new Processing with its default visualizer configuration.</remarks>
        public Processing()
        {
            _visualizerView = new ProcessingView();
            settingsViewModel.PropertyChanged += (sender, e) =>
            {
                if (_visualizerView?.DataContext is ProcessingViewModel vm)
                {
                    switch (e.PropertyName)
                    {
                        case nameof(ProcessingViewModel.Text):
                            _compiledSource = null;
                            _compiledProgram = null;
                            break;
                    }
                }
            };
            _visualizerView.DataContext = settingsViewModel;
        }

        private float prevX = 0;
        private float prevY = 0;
        private readonly float R = 0.995f; // カットオフ調整

        private float HighPass(float x)
        {
            float y = x - prevX + R * prevY;
            prevX = x;
            prevY = y;
            return y;
        }

        private void EnsureCompiled(string script)
        {
            script ??= string.Empty;
            if (string.Equals(_compiledSource, script, StringComparison.Ordinal))
                return;

            _compiledSource = script;
            try
            {
                _compiledProgram = ScriptParser.Parse(script);
            }
            catch
            {
                _compiledProgram = null;
            }
        }

        public List<XYPoint> ProcessAudio(WasapiCapture capture, WaveInEventArgs e)
        {
            var fmt = capture.WaveFormat;
            int inputSampleRate = fmt.SampleRate;

            float[] wav = IAudioVisualizer.ConvertToWav1ch(capture, e);

            //ハイパスフィルタ
            prevX = 0;
            prevY = 0;
            for (int i = 0; i < wav.Length; i++)
                wav[i] = HighPass(wav[i]);

            // FFT 用に複素数配列へ
            Complex32[] fft = new Complex32[wav.Length];
            for (int i = 0; i < wav.Length; i++)
                fft[i] = new Complex32(wav[i], 0);

            // FFT 実行
            Fourier.Forward(fft, FourierOptions.Matlab);

            // 振幅スペクトルへ
            float[] spectrum = new float[fft.Length / 2];
            for (int i = 0; i < spectrum.Length; i++)
                spectrum[i] = fft[i].Magnitude;

            float kick = IAudioVisualizer.GetBand(spectrum, 50, 100, inputSampleRate);
            float snare = IAudioVisualizer.GetBand(spectrum, 1500, 3000, inputSampleRate);
            float hat = IAudioVisualizer.GetBand(spectrum, 6000, 12000, inputSampleRate);

            EnsureCompiled(settingsViewModel.Text);
            if (_compiledProgram is null)
                return [];

            var vars = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["kick"] = kick,
                ["snare"] = snare,
                ["hat"] = hat,
                ["time"] = _sw.Elapsed.TotalSeconds,
                ["pi"] = Math.PI,
                ["tau"] = Math.PI * 2.0
            };

            try
            {
                return _compiledProgram.Execute(vars);
            }
            catch
            {
                return [];
            }
        }

        private sealed class ScriptProgram(List<IStatement> statements)
        {
            private readonly List<IStatement> _statements = statements;

            public List<XYPoint> Execute(Dictionary<string, double> vars)
            {
                var output = new List<XYPoint>();
                var context = new ExecutionContext(vars);

                foreach (var statement in _statements)
                    statement.Execute(context, output);

                return output;
            }
        }

        private sealed class ExecutionContext(Dictionary<string, double> vars)
        {
            private readonly Dictionary<string, double> _vars = vars;

            public double GetVariable(string name)
            {
                if (_vars.TryGetValue(name, out double value))
                    return value;
                return 0d;
            }

            public void SetVariable(string name, double value) => _vars[name] = value;
        }

        private interface IStatement
        {
            void Execute(ExecutionContext context, List<XYPoint> output);
        }

        private sealed class AssignmentStatement(string name, IExpression expression) : IStatement
        {
            private readonly string _name = name;
            private readonly IExpression _expression = expression;

            public void Execute(ExecutionContext context, List<XYPoint> output)
            {
                context.SetVariable(_name, _expression.Evaluate(context));
            }
        }

        private sealed class LineStatement(IExpression x1, IExpression y1, IExpression x2, IExpression y2, IExpression intensity) : IStatement
        {
            private readonly IExpression _x1 = x1;
            private readonly IExpression _y1 = y1;
            private readonly IExpression _x2 = x2;
            private readonly IExpression _y2 = y2;
            private readonly IExpression _intensity = intensity;

            public void Execute(ExecutionContext context, List<XYPoint> output)
            {
                double x1 = _x1.Evaluate(context);
                double y1 = _y1.Evaluate(context);
                double x2 = _x2.Evaluate(context);
                double y2 = _y2.Evaluate(context);
                double intensity = _intensity.Evaluate(context);

                output.Add(new XYPoint(x1, y1, intensity));
                output.Add(new XYPoint(x2, y2, intensity));
            }
        }

        private sealed class RectStatement(IExpression x, IExpression y, IExpression w, IExpression h, IExpression intensity) : IStatement
        {
            private readonly IExpression _x = x;
            private readonly IExpression _y = y;
            private readonly IExpression _w = w;
            private readonly IExpression _h = h;
            private readonly IExpression _intensity = intensity;

            public void Execute(ExecutionContext context, List<XYPoint> output)
            {
                double x = _x.Evaluate(context);
                double y = _y.Evaluate(context);
                double w = _w.Evaluate(context);
                double h = _h.Evaluate(context);
                double intensity = _intensity.Evaluate(context);

                AddRect(output, x, y, w, h, intensity);
            }
        }

        private sealed class EllipseStatement(IExpression x, IExpression y, IExpression w, IExpression h, IExpression intensity) : IStatement
        {
            private readonly IExpression _x = x;
            private readonly IExpression _y = y;
            private readonly IExpression _w = w;
            private readonly IExpression _h = h;
            private readonly IExpression _intensity = intensity;

            public void Execute(ExecutionContext context, List<XYPoint> output)
            {
                double x = _x.Evaluate(context);
                double y = _y.Evaluate(context);
                double w = _w.Evaluate(context);
                double h = _h.Evaluate(context);
                double intensity = _intensity.Evaluate(context);

                AddEllipse(output, x, y, w, h, 32, intensity);
            }
        }

        private sealed class CircleStatement(IExpression x, IExpression y, IExpression d, IExpression s, IExpression intensity) : IStatement
        {
            private readonly IExpression _x = x;
            private readonly IExpression _y = y;
            private readonly IExpression _d = d;
            private readonly IExpression _s = s;
            private readonly IExpression _intensity = intensity;

            public void Execute(ExecutionContext context, List<XYPoint> output)
            {
                double x = _x.Evaluate(context);
                double y = _y.Evaluate(context);
                double d = _d.Evaluate(context);
                double s = _s.Evaluate(context);
                double intensity = _intensity.Evaluate(context);

                AddEllipse(output, x, y, d, d, Math.Max(3, (int)Math.Round(s)), intensity);
            }
        }

        private sealed class TriangleStatement(IExpression x1, IExpression y1, IExpression x2, IExpression y2, IExpression x3, IExpression y3, IExpression intensity) : IStatement
        {
            private readonly IExpression _x1 = x1;
            private readonly IExpression _y1 = y1;
            private readonly IExpression _x2 = x2;
            private readonly IExpression _y2 = y2;
            private readonly IExpression _x3 = x3;
            private readonly IExpression _y3 = y3;
            private readonly IExpression _intensity = intensity;

            public void Execute(ExecutionContext context, List<XYPoint> output)
            {
                double x1 = _x1.Evaluate(context);
                double y1 = _y1.Evaluate(context);
                double x2 = _x2.Evaluate(context);
                double y2 = _y2.Evaluate(context);
                double x3 = _x3.Evaluate(context);
                double y3 = _y3.Evaluate(context);
                double intensity = _intensity.Evaluate(context);

                AddLine(output, x1, y1, x2, y2, intensity);
                AddLine(output, x2, y2, x3, y3, intensity);
                AddLine(output, x3, y3, x1, y1, intensity);
            }
        }

        private sealed class ArcStatement(IExpression x, IExpression y, IExpression w, IExpression h, IExpression start, IExpression stop, IExpression split, IExpression intensity) : IStatement
        {
            private readonly IExpression _x = x;
            private readonly IExpression _y = y;
            private readonly IExpression _w = w;
            private readonly IExpression _h = h;
            private readonly IExpression _start = start;
            private readonly IExpression _stop = stop;
            private readonly IExpression _split = split;
            private readonly IExpression _intensity = intensity;

            public void Execute(ExecutionContext context, List<XYPoint> output)
            {
                double x = _x.Evaluate(context);
                double y = _y.Evaluate(context);
                double w = _w.Evaluate(context);
                double h = _h.Evaluate(context);
                double start = _start.Evaluate(context);
                double stop = _stop.Evaluate(context);
                double split = _split.Evaluate(context);
                double intensity = _intensity.Evaluate(context);

                AddArc(output, x, y, w, h, start, stop, Math.Max(2, (int)Math.Round(split)), intensity);
            }
        }

        private sealed class PointStatement(IExpression x, IExpression y, IExpression size, IExpression intensity) : IStatement
        {
            private readonly IExpression _x = x;
            private readonly IExpression _y = y;
            private readonly IExpression _size = size;
            private readonly IExpression _intensity = intensity;

            public void Execute(ExecutionContext context, List<XYPoint> output)
            {
                double x = _x.Evaluate(context);
                double y = _y.Evaluate(context);
                double size = _size.Evaluate(context);
                double intensity = _intensity.Evaluate(context);
                AddPoint(output, x, y, size, intensity);
            }
        }

        private sealed class ForStatement(IStatement? init, IExpression condition, IStatement? step, List<IStatement> body) : IStatement
        {
            private readonly IStatement? _init = init;
            private readonly IExpression _condition = condition;
            private readonly IStatement? _step = step;
            private readonly List<IStatement> _body = body;

            public void Execute(ExecutionContext context, List<XYPoint> output)
            {
                _init?.Execute(context, output);

                const int maxLoopCount = 200000;
                int count = 0;
                while (_condition.Evaluate(context) != 0d)
                {
                    foreach (var statement in _body)
                        statement.Execute(context, output);

                    _step?.Execute(context, output);

                    count++;
                    if (count >= maxLoopCount)
                        break;
                }
            }
        }

        private sealed class ExpressionStatement(IExpression expression) : IStatement
        {
            private readonly IExpression _expression = expression;

            public void Execute(ExecutionContext context, List<XYPoint> output)
            {
                _expression.Evaluate(context);
            }
        }

        private static void AddLine(List<XYPoint> output, double x1, double y1, double x2, double y2, double intensity = 1d)
        {
            output.Add(new XYPoint(x1, y1, intensity));
            output.Add(new XYPoint(x2, y2, intensity));
        }

        private static void AddRect(List<XYPoint> output, double x, double y, double w, double h, double intensity = 1d)
        {
            double x2 = x + w;
            double y2 = y + h;

            AddLine(output, x, y, x2, y, intensity);
            AddLine(output, x2, y, x2, y2, intensity);
            AddLine(output, x2, y2, x, y2, intensity);
            AddLine(output, x, y2, x, y, intensity);
        }

        private static void AddEllipse(List<XYPoint> output, double cx, double cy, double w, double h, int split, double intensity = 1d)
        {
            split = Math.Max(3, split);
            double rx = w * 0.5;
            double ry = h * 0.5;
            double step = Math.PI * 2d / split;

            double px = cx + Math.Cos(0d) * rx;
            double py = cy + Math.Sin(0d) * ry;

            for (int i = 1; i <= split; i++)
            {
                double a = i * step;
                double x = cx + Math.Cos(a) * rx;
                double y = cy + Math.Sin(a) * ry;
                AddLine(output, px, py, x, y, intensity);
                px = x;
                py = y;
            }
        }

        private static void AddPoint(List<XYPoint> output, double x, double y, double size, double intensity = 1d)
        {
            double s = Math.Abs(size);
            if (s <= 0d)
                s = 0.002d;
            double h = s * 0.5d;
            AddLine(output, x - h, y, x + h, y, intensity);
        }

        private static void AddArc(List<XYPoint> output, double cx, double cy, double w, double h, double start, double stop, int split, double intensity = 1d)
        {
            split = Math.Max(2, split);
            double rx = w * 0.5;
            double ry = h * 0.5;
            double range = stop - start;

            double firstX = cx + Math.Cos(start) * rx;
            double firstY = cy + Math.Sin(start) * ry;
            AddLine(output, cx, cy, firstX, firstY, intensity);

            double prevX = firstX;
            double prevY = firstY;

            for (int i = 1; i <= split; i++)
            {
                double t = (double)i / split;
                double a = start + range * t;
                double x = cx + Math.Cos(a) * rx;
                double y = cy + Math.Sin(a) * ry;
                AddLine(output, prevX, prevY, x, y, intensity);
                prevX = x;
                prevY = y;
            }

            AddLine(output, prevX, prevY, cx, cy, intensity);
        }

        private interface IExpression
        {
            double Evaluate(ExecutionContext context);
        }

        private sealed class NumberExpression(double value) : IExpression
        {
            private readonly double _value = value;
            public double Evaluate(ExecutionContext context) => _value;
        }

        private sealed class VariableExpression(string name) : IExpression
        {
            private readonly string _name = name;
            public double Evaluate(ExecutionContext context) => context.GetVariable(_name);
        }

        private sealed class UnaryExpression(char op, IExpression inner) : IExpression
        {
            private readonly char _op = op;
            private readonly IExpression _inner = inner;

            public double Evaluate(ExecutionContext context)
            {
                double v = _inner.Evaluate(context);
                return _op switch
                {
                    '+' => v,
                    '-' => -v,
                    _ => throw new InvalidOperationException($"Unsupported unary operator: {_op}")
                };
            }
        }

        private sealed class BinaryExpression(char op, IExpression left, IExpression right) : IExpression
        {
            private readonly char _op = op;
            private readonly IExpression _left = left;
            private readonly IExpression _right = right;

            public double Evaluate(ExecutionContext context)
            {
                double l = _left.Evaluate(context);
                double r = _right.Evaluate(context);

                return _op switch
                {
                    '+' => l + r,
                    '-' => l - r,
                    '*' => l * r,
                    '/' => r == 0d ? 0d : l / r,
                    '%' => r == 0d ? 0d : l % r,
                    '^' => Math.Pow(l, r),
                    _ => throw new InvalidOperationException($"Unsupported binary operator: {_op}")
                };
            }
        }

        private sealed class ComparisonExpression(string op, IExpression left, IExpression right) : IExpression
        {
            private readonly string _op = op;
            private readonly IExpression _left = left;
            private readonly IExpression _right = right;

            public double Evaluate(ExecutionContext context)
            {
                double l = _left.Evaluate(context);
                double r = _right.Evaluate(context);

                bool result = _op switch
                {
                    "<" => l < r,
                    "<=" => l <= r,
                    ">" => l > r,
                    ">=" => l >= r,
                    "==" => l == r,
                    "!=" => l != r,
                    _ => throw new InvalidOperationException($"Unsupported comparison operator: {_op}")
                };

                return result ? 1d : 0d;
            }
        }

        private sealed class TernaryExpression(IExpression condition, IExpression whenTrue, IExpression whenFalse) : IExpression
        {
            private readonly IExpression _condition = condition;
            private readonly IExpression _whenTrue = whenTrue;
            private readonly IExpression _whenFalse = whenFalse;

            public double Evaluate(ExecutionContext context)
            {
                return _condition.Evaluate(context) != 0d
                    ? _whenTrue.Evaluate(context)
                    : _whenFalse.Evaluate(context);
            }
        }

        private sealed class PrefixUpdateExpression(string name, double delta) : IExpression
        {
            private readonly string _name = name;
            private readonly double _delta = delta;

            public double Evaluate(ExecutionContext context)
            {
                double value = context.GetVariable(_name) + _delta;
                context.SetVariable(_name, value);
                return value;
            }
        }

        private sealed class PostfixUpdateExpression(string name, double delta) : IExpression
        {
            private readonly string _name = name;
            private readonly double _delta = delta;

            public double Evaluate(ExecutionContext context)
            {
                double old = context.GetVariable(_name);
                context.SetVariable(_name, old + _delta);
                return old;
            }
        }

        private sealed class FunctionExpression(string name, List<IExpression> args) : IExpression
        {
            private readonly string _name = name;
            private readonly List<IExpression> _args = args;

            public double Evaluate(ExecutionContext context)
            {
                double Arg(int i) => _args[i].Evaluate(context);

                return _name.ToLowerInvariant() switch
                {
                    "sin" => Math.Sin(Arg(0)),
                    "cos" => Math.Cos(Arg(0)),
                    "tan" => Math.Tan(Arg(0)),
                    "asin" => Math.Asin(Arg(0)),
                    "acos" => Math.Acos(Arg(0)),
                    "atan" => Math.Atan(Arg(0)),
                    "atan2" => Math.Atan2(Arg(0), Arg(1)),
                    "sqrt" => Math.Sqrt(Math.Max(0, Arg(0))),
                    "pow" => Math.Pow(Arg(0), Arg(1)),
                    "abs" => Math.Abs(Arg(0)),
                    "min" => Math.Min(Arg(0), Arg(1)),
                    "max" => Math.Max(Arg(0), Arg(1)),
                    "floor" => Math.Floor(Arg(0)),
                    "ceil" => Math.Ceiling(Arg(0)),
                    "round" => Math.Round(Arg(0)),
                    "clamp" => Math.Clamp(Arg(0), Arg(1), Arg(2)),
                    "mag" => EvaluateMag(context),
                    _ => throw new InvalidOperationException($"Unknown function: {_name}")
                };
            }

            private double EvaluateMag(ExecutionContext context)
            {
                if (_args.Count == 2)
                {
                    double x = _args[0].Evaluate(context);
                    double y = _args[1].Evaluate(context);
                    return Math.Sqrt(x * x + y * y);
                }

                if (_args.Count == 3)
                {
                    double x = _args[0].Evaluate(context);
                    double y = _args[1].Evaluate(context);
                    double z = _args[2].Evaluate(context);
                    return Math.Sqrt(x * x + y * y + z * z);
                }

                throw new InvalidOperationException("mag(x, y) or mag(x, y, z) is required.");
            }
        }

        private static class ScriptParser
        {
            public static ScriptProgram Parse(string source)
            {
                var tokenizer = new Tokenizer(source);
                var parser = new Parser(tokenizer.Tokenize());
                return parser.ParseProgram();
            }

            private sealed class Parser(List<Token> tokens)
            {
                private readonly List<Token> _tokens = tokens;
                private int _index;

                public ScriptProgram ParseProgram()
                {
                    var statements = new List<IStatement>();

                    while (!Is(TokenKind.End))
                    {
                        SkipSemicolons();
                        if (Is(TokenKind.End))
                            break;

                        statements.Add(ParseStatement());
                        SkipSemicolons();
                    }

                    return new ScriptProgram(statements);
                }

                private IStatement ParseStatement()
                {
                    if (Current.Kind == TokenKind.Identifier && Current.Text.Equals("for", StringComparison.OrdinalIgnoreCase))
                        return ParseForStatement();

                    return ParseSimpleStatement();
                }

                private IStatement ParseSimpleStatement()
                {
                    if (Current.Kind == TokenKind.Identifier && Peek().Kind == TokenKind.Assign)
                    {
                        string name = Current.Text;
                        Next();
                        Expect(TokenKind.Assign);
                        var expr = ParseExpression();
                        return new AssignmentStatement(name, expr);
                    }

                    if (Current.Kind == TokenKind.Identifier && Peek().Kind == TokenKind.LParen)
                    {
                        string name = Current.Text;
                        Next();
                        Expect(TokenKind.LParen);
                        var args = ParseArguments();
                        return ParseDrawStatement(name, args);
                    }

                    return new ExpressionStatement(ParseExpression());
                }

                private IStatement ParseDrawStatement(string name, List<IExpression> args)
                {
                    if (name.Equals("line", StringComparison.OrdinalIgnoreCase))
                    {
                        if (args.Count != 4 && args.Count != 5)
                            throw new InvalidOperationException("line(x1, y1, x2, y2[, intensity]) is required.");

                        var intensity = args.Count == 5 ? args[4] : new NumberExpression(1d);
                        return new LineStatement(args[0], args[1], args[2], args[3], intensity);
                    }

                    if (name.Equals("point", StringComparison.OrdinalIgnoreCase))
                    {
                        if (args.Count != 2 && args.Count != 3 && args.Count != 4)
                            throw new InvalidOperationException("point(x, y[, size][, intensity]) is required.");

                        var size = args.Count >= 3 ? args[2] : new NumberExpression(0.002d);
                        var intensity = args.Count == 4 ? args[3] : new NumberExpression(1d);
                        return new PointStatement(args[0], args[1], size, intensity);
                    }

                    if (name.Equals("rect", StringComparison.OrdinalIgnoreCase))
                    {
                        if (args.Count != 4 && args.Count != 5)
                            throw new InvalidOperationException("rect(x, y, w, h[, intensity]) is required.");
                        var intensity = args.Count == 5 ? args[4] : new NumberExpression(1d);
                        return new RectStatement(args[0], args[1], args[2], args[3], intensity);
                    }

                    if (name.Equals("ellipse", StringComparison.OrdinalIgnoreCase))
                    {
                        if (args.Count != 4 && args.Count != 5)
                            throw new InvalidOperationException("ellipse(x, y, w, h[, intensity]) is required.");
                        var intensity = args.Count == 5 ? args[4] : new NumberExpression(1d);
                        return new EllipseStatement(args[0], args[1], args[2], args[3], intensity);
                    }

                    if (name.Equals("circle", StringComparison.OrdinalIgnoreCase))
                    {
                        if (args.Count != 4 && args.Count != 5)
                            throw new InvalidOperationException("circle(x, y, d, s[, intensity]) is required.");
                        var intensity = args.Count == 5 ? args[4] : new NumberExpression(1d);
                        return new CircleStatement(args[0], args[1], args[2], args[3], intensity);
                    }

                    if (name.Equals("triangle", StringComparison.OrdinalIgnoreCase))
                    {
                        if (args.Count != 6 && args.Count != 7)
                            throw new InvalidOperationException("triangle(x1, y1, x2, y2, x3, y3[, intensity]) is required.");
                        var intensity = args.Count == 7 ? args[6] : new NumberExpression(1d);
                        return new TriangleStatement(args[0], args[1], args[2], args[3], args[4], args[5], intensity);
                    }

                    if (name.Equals("arc", StringComparison.OrdinalIgnoreCase))
                    {
                        if (args.Count != 7 && args.Count != 8)
                            throw new InvalidOperationException("arc(x, y, w, h, start, stop, split[, intensity]) is required.");
                        var intensity = args.Count == 8 ? args[7] : new NumberExpression(1d);
                        return new ArcStatement(args[0], args[1], args[2], args[3], args[4], args[5], args[6], intensity);
                    }

                    throw new InvalidOperationException($"Unknown statement function: {name}");
                }

                private IStatement ParseForHeaderStatement()
                {
                    if (Current.Kind == TokenKind.Identifier && Peek().Kind == TokenKind.Assign)
                    {
                        string name = Current.Text;
                        Next();
                        Expect(TokenKind.Assign);
                        var expr = ParseExpression();
                        return new AssignmentStatement(name, expr);
                    }

                    if (Current.Kind == TokenKind.Identifier && Peek().Kind == TokenKind.LParen)
                    {
                        string name = Current.Text;
                        Next();
                        Expect(TokenKind.LParen);
                        var args = ParseArguments();
                        return ParseDrawStatement(name, args);
                    }

                    return new ExpressionStatement(ParseExpression());
                }

                private IStatement ParseForStatement()
                {
                    Next(); // for
                    Expect(TokenKind.LParen);

                    IStatement? init = null;
                    if (!Is(TokenKind.Semicolon))
                        init = ParseForHeaderStatement();
                    Expect(TokenKind.Semicolon);

                    IExpression condition = new NumberExpression(1d);
                    if (!Is(TokenKind.Semicolon))
                        condition = ParseExpression();
                    Expect(TokenKind.Semicolon);

                    IStatement? step = null;
                    if (!Is(TokenKind.RParen))
                        step = ParseForHeaderStatement();
                    Expect(TokenKind.RParen);

                    var body = new List<IStatement>();
                    if (Match(TokenKind.LBrace))
                    {
                        while (!Is(TokenKind.RBrace) && !Is(TokenKind.End))
                        {
                            SkipSemicolons();
                            if (Is(TokenKind.RBrace) || Is(TokenKind.End))
                                break;
                            body.Add(ParseStatement());
                            SkipSemicolons();
                        }
                        Expect(TokenKind.RBrace);
                    }
                    else
                    {
                        body.Add(ParseStatement());
                    }

                    return new ForStatement(init, condition, step, body);
                }

                private List<IExpression> ParseArguments()
                {
                    var args = new List<IExpression>();
                    if (Match(TokenKind.RParen))
                        return args;

                    while (true)
                    {
                        args.Add(ParseExpression());
                        if (Match(TokenKind.RParen))
                            break;
                        Expect(TokenKind.Comma);
                    }

                    return args;
                }

                private IExpression ParseExpression() => ParseConditional();

                private IExpression ParseConditional()
                {
                    IExpression condition = ParseComparison();
                    if (Match(TokenKind.Question))
                    {
                        IExpression whenTrue = ParseExpression();
                        Expect(TokenKind.Colon);
                        IExpression whenFalse = ParseConditional();
                        return new TernaryExpression(condition, whenTrue, whenFalse);
                    }

                    return condition;
                }

                private IExpression ParseComparison()
                {
                    IExpression left = ParseAddSub();
                    while (Is(TokenKind.Less) || Is(TokenKind.LessEqual) || Is(TokenKind.Greater) || Is(TokenKind.GreaterEqual) || Is(TokenKind.EqualEqual) || Is(TokenKind.NotEqual))
                    {
                        string op = Current.Text;
                        Next();
                        IExpression right = ParseAddSub();
                        left = new ComparisonExpression(op, left, right);
                    }
                    return left;
                }

                private IExpression ParseAddSub()
                {
                    IExpression left = ParseMulDiv();
                    while (Is(TokenKind.Plus) || Is(TokenKind.Minus))
                    {
                        char op = Current.Text[0];
                        Next();
                        IExpression right = ParseMulDiv();
                        left = new BinaryExpression(op, left, right);
                    }
                    return left;
                }

                private IExpression ParseMulDiv()
                {
                    IExpression left = ParsePower();
                    while (Is(TokenKind.Star) || Is(TokenKind.Slash) || Is(TokenKind.Percent))
                    {
                        char op = Current.Text[0];
                        Next();
                        IExpression right = ParsePower();
                        left = new BinaryExpression(op, left, right);
                    }
                    return left;
                }

                private IExpression ParsePower()
                {
                    IExpression left = ParseUnary();
                    if (Is(TokenKind.Caret))
                    {
                        Next();
                        IExpression right = ParsePower();
                        left = new BinaryExpression('^', left, right);
                    }
                    return left;
                }

                private IExpression ParseUnary()
                {
                    if (Is(TokenKind.Plus) || Is(TokenKind.Minus))
                    {
                        char op = Current.Text[0];
                        Next();
                        return new UnaryExpression(op, ParseUnary());
                    }

                    if (Is(TokenKind.PlusPlus) || Is(TokenKind.MinusMinus))
                    {
                        bool increment = Is(TokenKind.PlusPlus);
                        Next();
                        if (Current.Kind != TokenKind.Identifier)
                            throw new InvalidOperationException("Prefix ++/-- requires identifier.");

                        string name = Current.Text;
                        Next();
                        return new PrefixUpdateExpression(name, increment ? 1d : -1d);
                    }

                    return ParsePrimary();
                }

                private IExpression ParsePrimary()
                {
                    if (Current.Kind == TokenKind.Number)
                    {
                        double value = Current.NumberValue;
                        Next();
                        return new NumberExpression(value);
                    }

                    if (Current.Kind == TokenKind.Identifier)
                    {
                        string name = Current.Text;
                        Next();

                        if (Match(TokenKind.LParen))
                        {
                            var args = ParseArguments();
                            return new FunctionExpression(name, args);
                        }

                        if (Match(TokenKind.PlusPlus))
                            return new PostfixUpdateExpression(name, 1d);

                        if (Match(TokenKind.MinusMinus))
                            return new PostfixUpdateExpression(name, -1d);

                        return new VariableExpression(name);
                    }

                    if (Match(TokenKind.LParen))
                    {
                        var expr = ParseExpression();
                        Expect(TokenKind.RParen);
                        return expr;
                    }

                    throw new InvalidOperationException("Invalid expression.");
                }

                private Token Current => _tokens[_index];
                private Token Peek(int offset = 1)
                {
                    int i = _index + offset;
                    if (i < 0)
                        i = 0;
                    if (i >= _tokens.Count)
                        i = _tokens.Count - 1;
                    return _tokens[i];
                }

                private void Next()
                {
                    if (_index < _tokens.Count - 1)
                        _index++;
                }

                private bool Match(TokenKind kind)
                {
                    if (!Is(kind))
                        return false;
                    Next();
                    return true;
                }

                private bool Is(TokenKind kind) => Current.Kind == kind;

                private void Expect(TokenKind kind)
                {
                    if (!Match(kind))
                        throw new InvalidOperationException($"Expected token: {kind}");
                }

                private void SkipSemicolons()
                {
                    while (Match(TokenKind.Semicolon))
                    {
                    }
                }
            }

            private sealed class Tokenizer(string source)
            {
                private readonly string _source = source;
                private int _index;

                public List<Token> Tokenize()
                {
                    var tokens = new List<Token>();
                    while (!End)
                    {
                        char c = _source[_index];

                        if (c == '\r')
                        {
                            _index++;
                            continue;
                        }

                        if (c == '\n')
                        {
                            if (ShouldInsertImplicitSemicolon(tokens))
                                tokens.Add(new Token(TokenKind.Semicolon, ";"));
                            _index++;
                            continue;
                        }

                        if (c == ';')
                        {
                            tokens.Add(new Token(TokenKind.Semicolon, ";"));
                            _index++;
                            continue;
                        }

                        if (char.IsWhiteSpace(c))
                        {
                            _index++;
                            continue;
                        }

                        if (c == '/' && PeekChar() == '/')
                        {
                            _index += 2;
                            while (!End && _source[_index] != '\n' && _source[_index] != '\r')
                                _index++;
                            continue;
                        }

                        if (c == '#')
                        {
                            _index++;
                            while (!End && _source[_index] != '\n' && _source[_index] != '\r')
                                _index++;
                            continue;
                        }

                        if (char.IsLetter(c) || c == '_')
                        {
                            int start = _index;
                            _index++;
                            while (!End && (char.IsLetterOrDigit(_source[_index]) || _source[_index] == '_'))
                                _index++;

                            string ident = _source[start.._index];
                            tokens.Add(new Token(TokenKind.Identifier, ident));
                            continue;
                        }

                        if (char.IsDigit(c) || c == '.')
                        {
                            int start = _index;
                            bool hasDot = c == '.';
                            _index++;

                            while (!End)
                            {
                                char n = _source[_index];
                                if (char.IsDigit(n))
                                {
                                    _index++;
                                    continue;
                                }

                                if (n == '.' && !hasDot)
                                {
                                    hasDot = true;
                                    _index++;
                                    continue;
                                }

                                if (n == 'e' || n == 'E')
                                {
                                    _index++;
                                    if (!End && (_source[_index] == '+' || _source[_index] == '-'))
                                        _index++;
                                    continue;
                                }

                                break;
                            }

                            string text = _source[start.._index];
                            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                                throw new InvalidOperationException($"Invalid number: {text}");

                            tokens.Add(new Token(TokenKind.Number, text, number));
                            continue;
                        }

                        if (c == '+' && PeekChar() == '+')
                        {
                            tokens.Add(new Token(TokenKind.PlusPlus, "++"));
                            _index += 2;
                            continue;
                        }

                        if (c == '-' && PeekChar() == '-')
                        {
                            tokens.Add(new Token(TokenKind.MinusMinus, "--"));
                            _index += 2;
                            continue;
                        }

                        if (c == '=' && PeekChar() == '=')
                        {
                            tokens.Add(new Token(TokenKind.EqualEqual, "=="));
                            _index += 2;
                            continue;
                        }

                        if (c == '!' && PeekChar() == '=')
                        {
                            tokens.Add(new Token(TokenKind.NotEqual, "!="));
                            _index += 2;
                            continue;
                        }

                        if (c == '<' && PeekChar() == '=')
                        {
                            tokens.Add(new Token(TokenKind.LessEqual, "<="));
                            _index += 2;
                            continue;
                        }

                        if (c == '>' && PeekChar() == '=')
                        {
                            tokens.Add(new Token(TokenKind.GreaterEqual, ">="));
                            _index += 2;
                            continue;
                        }

                        tokens.Add(c switch
                        {
                            '+' => new Token(TokenKind.Plus, "+"),
                            '-' => new Token(TokenKind.Minus, "-"),
                            '*' => new Token(TokenKind.Star, "*"),
                            '/' => new Token(TokenKind.Slash, "/"),
                            '%' => new Token(TokenKind.Percent, "%"),
                            '^' => new Token(TokenKind.Caret, "^"),
                            '=' => new Token(TokenKind.Assign, "="),
                            '<' => new Token(TokenKind.Less, "<"),
                            '>' => new Token(TokenKind.Greater, ">"),
                            '?' => new Token(TokenKind.Question, "?"),
                            ':' => new Token(TokenKind.Colon, ":"),
                            '(' => new Token(TokenKind.LParen, "("),
                            ')' => new Token(TokenKind.RParen, ")"),
                            '{' => new Token(TokenKind.LBrace, "{"),
                            '}' => new Token(TokenKind.RBrace, "}"),
                            ',' => new Token(TokenKind.Comma, ","),
                            _ => throw new InvalidOperationException($"Unexpected character: {c}")
                        });

                        _index++;
                    }

                    tokens.Add(new Token(TokenKind.End, string.Empty));
                    return tokens;
                }

                private bool ShouldInsertImplicitSemicolon(List<Token> tokens)
                {
                    if (tokens.Count == 0)
                        return false;

                    var prev = tokens[^1].Kind;
                    bool prevCanEndStatement = prev == TokenKind.Identifier
                        || prev == TokenKind.Number
                        || prev == TokenKind.RParen
                        || prev == TokenKind.RBrace
                        || prev == TokenKind.PlusPlus
                        || prev == TokenKind.MinusMinus;

                    if (!prevCanEndStatement)
                        return false;

                    char next = PeekNextSignificantChar(_index + 1);
                    if (next == '\0')
                        return false;

                    if (next == '+' || next == '-' || next == '*' || next == '/' || next == '%' ||
                        next == '^' || next == '?' || next == ':' || next == ',' || next == ')' ||
                        next == '<' || next == '>' || next == '=' || next == '!' || next == '{')
                        return false;

                    return true;
                }

                private char PeekNextSignificantChar(int start)
                {
                    int i = start;
                    while (i < _source.Length)
                    {
                        char c = _source[i];

                        if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                        {
                            i++;
                            continue;
                        }

                        if (c == '/' && i + 1 < _source.Length && _source[i + 1] == '/')
                        {
                            i += 2;
                            while (i < _source.Length && _source[i] != '\n' && _source[i] != '\r')
                                i++;
                            continue;
                        }

                        if (c == '#')
                        {
                            i++;
                            while (i < _source.Length && _source[i] != '\n' && _source[i] != '\r')
                                i++;
                            continue;
                        }

                        return c;
                    }

                    return '\0';
                }

                private char PeekChar(int offset = 1)
                {
                    int i = _index + offset;
                    if (i < 0 || i >= _source.Length)
                        return '\0';
                    return _source[i];
                }

                private bool End => _index >= _source.Length;
            }

            private enum TokenKind
            {
                End,
                Identifier,
                Number,
                Plus,
                Minus,
                Star,
                Slash,
                Percent,
                Caret,
                Assign,
                LParen,
                RParen,
                LBrace,
                RBrace,
                Comma,
                Semicolon,
                Less,
                LessEqual,
                Greater,
                GreaterEqual,
                EqualEqual,
                NotEqual,
                Question,
                Colon,
                PlusPlus,
                MinusMinus
            }

            private readonly struct Token(TokenKind kind, string text, double numberValue = 0d)
            {
                public TokenKind Kind { get; } = kind;
                public string Text { get; } = text;
                public double NumberValue { get; } = numberValue;
            }
        }


        public void SaveSettings()
        {
            try
            {
                var json = JsonSerializer.Serialize(new { settingsViewModel.Text });

                string settingsPath = IAudioVisualizer.GetSettingsPath(VisualizerName);

                File.WriteAllText(settingsPath, json);
            }
            catch { }
        }

        public void LoadSettings()
        {
            try
            {
                string settingsPath = IAudioVisualizer.GetSettingsPath(VisualizerName);

                if (!File.Exists(settingsPath))
                    return;

                var json = File.ReadAllText(settingsPath);
                var data = JsonSerializer.Deserialize<SettingsData>(json);

                if (data != null)
                {
                    settingsViewModel.Text = data.Text;
                }
            }
            catch { }
        }

        private class SettingsData
        {
            public string Text { get; set; } = "";
        }

    }
}
