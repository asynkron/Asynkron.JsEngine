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
const revealPackedTriangles = new Array(revealFaces.length * 18);

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

function revealParseHex(hex) {
  return {
    r: parseInt(hex.substring(1, 3), 16),
    g: parseInt(hex.substring(3, 5), 16),
    b: parseInt(hex.substring(5, 7), 16)
  };
}

function revealClampByte(value) {
  return Math.max(0, Math.min(255, Math.round(value)));
}

const revealFaceColorChannels = revealFaceColors.map(revealParseHex);
const revealLightDirection = revealNormalize({ x: -0.35, y: -0.62, z: 0.7 });
const revealHighlightColor = { r: 255, g: 255, b: 255 };
const revealShadowColor = { r: 0, g: 2, b: 7 };

function revealNormalize(vertex) {
  const length = Math.sqrt(vertex.x * vertex.x + vertex.y * vertex.y + vertex.z * vertex.z) || 1;
  return {
    x: vertex.x / length,
    y: vertex.y / length,
    z: vertex.z / length
  };
}

function revealWriteMixedColor(output, offset, from, to, amount) {
  const blend = Math.max(0, Math.min(1, amount));
  output[offset] = revealClampByte(from.r + (to.r - from.r) * blend);
  output[offset + 1] = revealClampByte(from.g + (to.g - from.g) * blend);
  output[offset + 2] = revealClampByte(from.b + (to.b - from.b) * blend);
  output[offset + 3] = 255;
}

function revealGetLightSource(elapsed) {
  const t = elapsed * 0.00115;
  return {
    x: Math.cos(t) * 2.6 - 0.55,
    y: Math.sin(t * 0.8) * 1.9 - 1.15,
    z: 3.15 + Math.sin(t * 1.12) * 0.55
  };
}

function revealPointLightAmount(vertex, normal, lightSource) {
  const lx = lightSource.x - vertex.x;
  const ly = lightSource.y - vertex.y;
  const lz = lightSource.z - vertex.z;
  const distance = Math.sqrt(lx * lx + ly * ly + lz * lz) || 1;
  const facing = Math.max(0, normal.x * lx / distance + normal.y * ly / distance + normal.z * lz / distance);
  const attenuation = 1 / (1 + distance * distance * 0.16);
  return facing * attenuation * 2.75;
}

function revealWriteShadedVertex(output, offset, point, base, vertex, faceLight, vertexIndex, lightSource) {
  output[offset] = point.x;
  output[offset + 1] = point.y;

  const normal = revealNormalize(vertex);
  const directional = Math.max(
    0,
    normal.x * revealLightDirection.x + normal.y * revealLightDirection.y + normal.z * revealLightDirection.z);
  const rim = Math.max(0, 1 - Math.abs(normal.z));
  const alternating = vertexIndex === 0 ? 0.13 : vertexIndex === 1 ? -0.06 : 0.04;
  const pointLight = revealPointLightAmount(vertex, normal, lightSource);
  const shade = Math.max(0, Math.min(1.95, faceLight * 0.28 + directional * 0.24 + rim * 0.08 + pointLight + alternating));

  if (shade >= 1) {
    revealWriteMixedColor(output, offset + 2, base, revealHighlightColor, Math.min(0.95, (shade - 1) * 1.18));
    return;
  }

  revealWriteMixedColor(output, offset + 2, base, revealShadowColor, 1 - shade);
}

function revealPatchNativeMeshCaption(ctx) {
  ctx.svg.layer.rect("reveal-native-mesh-caption-patch", 79, 450, 330, 22, "#0f1d2f", 1);
  ctx.svg.layer.text("reveal-native-mesh-caption", "perspective projection -> point-lit vertex mesh", 84, 462, 14, "#9ca3af", 1);
  ctx.svg.layer.circle("reveal-light-glow", 0, 0, 28, "#ffc000", 0.14);
  ctx.svg.layer.circle("reveal-light-core", 0, 0, 7, "#fff2a8", 0.95);
  ctx.svg.layer.text("reveal-light-label", "light", 0, 0, 13, "#fff2a8", 0.75);
}

function revealUpdateLightMarker(lightSource) {
  const lightPoint = revealProject(lightSource);
  const glow = svg.id("reveal-light-glow");
  glow.set("cx", lightPoint.x);
  glow.set("cy", lightPoint.y);
  glow.set("r", 23 + lightPoint.scale * 12);

  const core = svg.id("reveal-light-core");
  core.set("cx", lightPoint.x);
  core.set("cy", lightPoint.y);
  core.set("r", 5 + lightPoint.scale * 3);

  const label = svg.id("reveal-light-label");
  label.set("x", lightPoint.x + 13);
  label.set("y", lightPoint.y - 10);
}

function revealRenderMesh(ctx, elapsed) {
  const ax = elapsed * 0.00062;
  const ay = elapsed * 0.00086;
  const az = elapsed * 0.00031;
  const transformed = [];
  const projected = [];
  const lightSource = revealGetLightSource(elapsed);
  revealUpdateLightMarker(lightSource);

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
      points: [projected[face[0]], projected[face[1]], projected[face[2]]],
      vertices: [transformed[face[0]], transformed[face[1]], transformed[face[2]]]
    });
  }

  faces.sort(function (a, b) {
    return a.depth - b.depth;
  });

  let writeIndex = 0;
  for (let drawIndex = 0; drawIndex < faces.length; drawIndex++) {
    const face = faces[drawIndex];
    const base = revealFaceColorChannels[face.index % revealFaceColorChannels.length];
    const light = Math.max(0.28, Math.min(1, 0.54 + face.normalZ * 0.18 + face.depth * 0.08));

    revealWriteShadedVertex(revealPackedTriangles, writeIndex, face.points[0], base, face.vertices[0], light, 0, lightSource);
    writeIndex += 6;
    revealWriteShadedVertex(revealPackedTriangles, writeIndex, face.points[1], base, face.vertices[1], light, 1, lightSource);
    writeIndex += 6;
    revealWriteShadedVertex(revealPackedTriangles, writeIndex, face.points[2], base, face.vertices[2], light, 2, lightSource);
    writeIndex += 6;
  }

  nativeMesh.trianglesRgbaArray(revealPackedTriangles);
}

slideScript("jsengine-presentation-reveal.svg", {
  enter: function (ctx) {
    nativeMesh.clear();
    revealPatchNativeMeshCaption(ctx);
  },

  frame: function (ctx, time, elapsed) {
    revealRenderMesh(ctx, elapsed);
  },

  leave: function (ctx) {
    nativeMesh.clear();
  }
});
