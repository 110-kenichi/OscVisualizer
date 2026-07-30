from pathlib import Path
import urllib.request
import ssl
import cv2
import numpy as np

root = Path(__file__).resolve().parents[1]
tmp = Path(__file__).resolve().parent / "kamon-images"
tmp.mkdir(exist_ok=True)
base = "https://www.hasegawa.jp/cdn/shop/t/55/assets/"
file_numbers = list(range(1, 94)) + list(range(96, 101)) + [103, 104]
ssl_context = ssl._create_unverified_context()

for number in file_numbers:
	path = tmp / f"kamon-{number:02d}.jpg"
	if not path.exists():
		url = f"{base}kamon-{number:02d}_grande.jpg"
		try:
			request = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
			with urllib.request.urlopen(request, context=ssl_context) as response:
				path.write_bytes(response.read())
		except Exception as exc:
			print(f"download failed {number}: {exc}")

traces = []
for number in file_numbers:
	path = tmp / f"kamon-{number:02d}.jpg"
	image = cv2.imread(str(path), cv2.IMREAD_GRAYSCALE)
	if image is None:
		traces.append([])
		continue

	_, binary = cv2.threshold(image, 150, 255, cv2.THRESH_BINARY_INV)
	contours, _ = cv2.findContours(binary, cv2.RETR_LIST, cv2.CHAIN_APPROX_SIMPLE)
	selected = []
	for contour in contours:
		area = cv2.contourArea(contour)
		if area < 12:
			continue
		perimeter = cv2.arcLength(contour, True)
		epsilon = max(1.0, perimeter * 0.004)
		polygon = cv2.approxPolyDP(contour, epsilon, True)
		if len(polygon) >= 3:
			selected.append(polygon.reshape(-1, 2))

	if not selected:
		traces.append([])
		continue

	all_points = np.concatenate(selected).astype(np.float64)
	min_x, min_y = all_points.min(axis=0)
	max_x, max_y = all_points.max(axis=0)
	center_x = (min_x + max_x) * 0.5
	center_y = (min_y + max_y) * 0.5
	scale = max(max_x - min_x, max_y - min_y) * 0.5
	scale = max(scale, 1.0)

	normalized = []
	total = 0
	for polygon in selected:
		line = []
		for x, y in polygon:
			line.append(((x - center_x) / scale * 0.84, (center_y - y) / scale * 0.84))
		if len(line) >= 3:
			normalized.append(line)
			total += len(line)

	while total > 1100:
		changed = False
		for i, line in enumerate(normalized):
			if len(line) > 4:
				normalized[i] = line[::2]
				total -= len(line) - len(normalized[i])
				changed = True
		if not changed:
			break

	traces.append(normalized)

out = root / "OscTest" / "Services" / "KamonTraces.cs"
with out.open("w", encoding="utf-8") as f:
	f.write("using OscVisualizer.Models;\nusing System.Collections.Generic;\n\nnamespace OscVisualizer.Services;\n\ninternal static class KamonTraces\n{\n    internal static bool TryAdd(int index, List<XYPoint> points)\n    {\n        if (index < 0 || index >= Data.Length || Data[index].Length == 0)\n            return false;\n\n        foreach (var trace in Data[index])\n        {\n            for (int i = 0; i < trace.Length; i++)\n            {\n                var a = trace[i];\n                var b = trace[(i + 1) % trace.Length];\n                points.Add(new XYPoint(a.x, a.y, 0.25));\n                points.Add(new XYPoint(b.x, b.y, 0.8));\n            }\n        }\n        return true;\n    }\n\n    private static readonly (double x, double y)[][][] Data =\n    [\n")
	for i, trace_set in enumerate(traces):
		f.write("        [\n")
		for line in trace_set:
			values = ", ".join(f"({x:.6f}, {y:.6f})" for x, y in line)
			f.write(f"            new (double x, double y)[] {{ {values} }},\n")
		f.write("        ],\n")
	f.write("    ];\n}\n")
print(f"generated {out} with {len(traces)} traces")
