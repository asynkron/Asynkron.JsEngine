const revealPhi = (1 + Math.sqrt(5)) / 2;
const revealVertices = [
  [-1, revealPhi, 0],
  [1, revealPhi, 0],
  [-1, -revealPhi, 0],
  [1, -revealPhi, 0],
  [0, -1, revealPhi],
  [0, 1, revealPhi],
  [0, -1, -revealPhi],
  [0, 1, -revealPhi],
  [revealPhi, 0, -1],
  [revealPhi, 0, 1],
  [-revealPhi, 0, -1],
  [-revealPhi, 0, 1]
];

const revealFaces = [
  [0, 11, 5],
  [0, 5, 1],
  [0, 1, 7],
  [0, 7, 10],
  [0, 10, 11],
  [1, 5, 9],
  [5, 11, 4],
  [11, 10, 2],
  [10, 7, 6],
  [7, 1, 8],
  [3, 9, 4],
  [3, 4, 2],
  [3, 2, 6],
  [3, 6, 8],
  [3, 8, 9],
  [4, 9, 5],
  [2, 4, 11],
  [6, 2, 10],
  [8, 6, 7],
  [9, 8, 1]
];

const revealFaceColors = [
  "#00b0f0",
  "#00d4ff",
  "#34d399",
  "#ffc000",
  "#ff4f81",
  "#a78bfa"
];

function revealRotateVertex(vertex, ax, ay, az) {
  let x = vertex[0];
  let y = vertex[1];
  let z = vertex[2];

  const cx = Math.cos(ax);
  const sx = Math.sin(ax);
  const cy = Math.cos(ay);
  const sy = Math.sin(ay);
  const cz = Math.cos(az);
  const sz = Math.sin(az);

  let y1 = y * cx - z * sx;
  let z1 = y * sx + z * cx;
  y = y1;
  z = z1;

  let x1 = x * cy + z * sy;
  z1 = -x * sy + z * cy;
  x = x1;
  z = z1;

  x1 = x * cz - y * sz;
  y1 = x * sz + y * cz;

  return { x: x1, y: y1, z: z };
}

function revealProject(vertex) {
  const cameraDistance = 5.2;
  const perspective = cameraDistance / (cameraDistance - vertex.z);
  return {
    x: 676 + vertex.x * 96 * perspective,
    y: 284 + vertex.y * 96 * perspective,
    z: vertex.z,
    scale: perspective
  };
}

function revealNormal(a, b, c) {
  const ux = b.x - a.x;
  const uy = b.y - a.y;
  const uz = b.z - a.z;
  const vx = c.x - a.x;
  const vy = c.y - a.y;
  const vz = c.z - a.z;
  return {
    x: uy * vz - uz * vy,
    y: uz * vx - ux * vz,
    z: ux * vy - uy * vx
  };
}

function revealPolygonPoints(points) {
  let text = "";
  for (let index = 0; index < points.length; index++) {
    if (index > 0) {
      text += " ";
    }

    text += points[index].x.toFixed(2) + "," + points[index].y.toFixed(2);
  }

  return text;
}

function revealCreateFace(ctx, index) {
  const color = revealFaceColors[index % revealFaceColors.length];
  ctx.svg.layer.polygon("reveal-face-" + index, "0,0 1,0 0,1", color, "#e5f7ff", 1.15, 0);
}

function revealCreateEdges(ctx) {
  for (let index = 0; index < revealFaces.length; index++) {
    ctx.svg.layer.polygon("reveal-edge-" + index, "0,0 1,0 0,1", "none", "#d8f6ff", 1.2, 0);
  }
}

function revealRenderMesh(ctx, elapsed) {
  const ax = elapsed * 0.00062;
  const ay = elapsed * 0.00086;
  const az = elapsed * 0.00031;
  const transformed = [];
  const projected = [];

  for (let index = 0; index < revealVertices.length; index++) {
    const rotated = revealRotateVertex(revealVertices[index], ax, ay, az);
    transformed[index] = rotated;
    projected[index] = revealProject(rotated);
  }

  const faces = [];
  for (let index = 0; index < revealFaces.length; index++) {
    const face = revealFaces[index];
    const a = transformed[face[0]];
    const b = transformed[face[1]];
    const c = transformed[face[2]];
    const normal = revealNormal(a, b, c);
    const depth = (a.z + b.z + c.z) / 3;
    faces.push({
      index: index,
      depth: depth,
      normalZ: normal.z,
      points: [projected[face[0]], projected[face[1]], projected[face[2]]]
    });
  }

  faces.sort(function (a, b) {
    return a.depth - b.depth;
  });

  for (let drawIndex = 0; drawIndex < faces.length; drawIndex++) {
    const face = faces[drawIndex];
    const id = "reveal-face-" + drawIndex;
    const base = revealFaceColors[face.index % revealFaceColors.length];
    const light = Math.max(0.28, Math.min(1, 0.54 + face.normalZ * 0.18 + face.depth * 0.08));
    const opacity = (0.54 + light * 0.36).toFixed(3);
    ctx.svg.id(id).set("points", revealPolygonPoints(face.points));
    ctx.svg.id(id).set("fill", base);
    ctx.svg.id(id).set("opacity", opacity);
    ctx.svg.id(id).set("stroke-width", (0.65 + light * 1.1).toFixed(2));
  }

  for (let drawIndex = 0; drawIndex < faces.length; drawIndex++) {
    const face = faces[drawIndex];
    const id = "reveal-edge-" + drawIndex;
    ctx.svg.id(id).set("points", revealPolygonPoints(face.points));
    ctx.svg.id(id).set("opacity", (0.18 + drawIndex / faces.length * 0.38).toFixed(3));
  }
}

slideScript("jsengine-presentation-reveal.svg", {
  enter: function (ctx) {
    for (let index = 0; index < revealFaces.length; index++) {
      revealCreateFace(ctx, index);
    }

    revealCreateEdges(ctx);
  },

  frame: function (ctx, time, elapsed) {
    revealRenderMesh(ctx, elapsed);
  }
});
