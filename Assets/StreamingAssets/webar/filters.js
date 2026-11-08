// Camera-only colour filters for the AR page. The fragment shader replaces the plain one in XR8's camera
// pass, so there is no extra render pass and the 3D model (drawn afterwards) is never affected.
// Only cheap per-pixel maths: no blur, no extra textures, no pow().

export const FILTER_FRAGMENT = `
precision mediump float;
varying vec2 texUv;
uniform sampler2D sampler;
uniform float uOn;
uniform vec4 uA;   // exposure, contrast, saturation, warmth
uniform vec4 uB;   // shadows, highlights, green tint, fade
uniform vec4 uMix; // colour wash rgb, amount
uniform float uVig;
void main() {
  vec4 t = texture2D(sampler, texUv);
  if (uOn < 0.5) { gl_FragColor = t; return; }
  vec3 w = vec3(0.2126, 0.7152, 0.0722);
  vec3 c = t.rgb * (1.0 + uA.x);
  float l = dot(c, w);
  float dark = clamp(1.0 - l, 0.0, 1.0);
  float light = clamp(l, 0.0, 1.0);
  c += uB.x * 0.40 * dark * dark + uB.y * 0.35 * light * light;
  c = (c - 0.5) * (1.0 + uA.y) + 0.5;
  c = mix(vec3(dot(c, w)), c, 1.0 + uA.z);
  c.r += uA.w * 0.09;
  c.g += uB.z * 0.07;
  c.b -= uA.w * 0.09;
  c = mix(c, dot(c, w) * uMix.rgb * 1.25, uMix.a);
  c = mix(c, vec3(0.13, 0.13, 0.15) + c * 0.87, uB.w);
  vec2 v = texUv - 0.5;
  c *= 1.0 - uVig * dot(v, v) * 1.7;
  gl_FragColor = vec4(clamp(c, 0.0, 1.0), t.a);
}
`

// b exposure · k contrast · s saturation · w warmth · t green(+)/magenta(-) · sh shadows · hi highlights
// f fade · v vignette · mix [r, g, b, amount]
export const PRESETS = [
  { name: 'Original', swatch: 'linear-gradient(135deg,#59616b,#aab3bd)', p: {} },
  { name: 'Vivid', swatch: 'linear-gradient(135deg,#ff3d6e,#ffc400 55%,#00d0ff)', p: { k: 0.15, s: 0.35 } },
  { name: 'Golden', swatch: 'linear-gradient(135deg,#ffb347,#ffdf8a)', p: { b: 0.05, w: 0.6, s: 0.1, sh: 0.1 } },
  { name: 'Arctic', swatch: 'linear-gradient(135deg,#4fb6ff,#d9f2ff)', p: { w: -0.6, s: 0.05, k: 0.05 } },
  { name: 'Film', swatch: 'linear-gradient(135deg,#6f6a5c,#d6c7a6)', p: { f: 0.2, k: 0.1, s: -0.1, w: 0.1, sh: 0.1 } },
  { name: 'Retro', swatch: 'linear-gradient(135deg,#a8743f,#e6c98f)', p: { w: 0.35, f: 0.35, s: -0.2, t: 0.1, v: 0.3 } },
  { name: 'Dusk', swatch: 'linear-gradient(135deg,#6a3fa0,#ff8a70)', p: { w: 0.25, t: -0.35, k: 0.1, s: 0.1, v: 0.25 } },
  { name: 'Dream', swatch: 'linear-gradient(135deg,#ffc6e0,#c8d8ff)', p: { b: 0.12, f: 0.15, s: 0.05, hi: -0.1, t: -0.1 } },
  { name: 'Neon', swatch: 'linear-gradient(135deg,#0a3a55,#3ef2ff)', p: { k: 0.2, s: 0.2, v: 0.35, mix: [0.2, 0.85, 1, 0.25] } },
  { name: 'Sepia', swatch: 'linear-gradient(135deg,#6b4a2b,#e2c49a)', p: { k: 0.05, v: 0.2, mix: [1, 0.78, 0.55, 0.8] } },
  { name: 'Noir', swatch: 'linear-gradient(135deg,#050505,#8a8a8a)', p: { s: -1, k: 0.35, v: 0.5, sh: -0.15 } },
  { name: 'Silver', swatch: 'linear-gradient(135deg,#7d7d7d,#ececec)', p: { s: -1, f: 0.25, b: 0.05 } },
]

export const ADJUSTMENTS = [
  { key: 'brightness', label: 'Brightness' },
  { key: 'contrast', label: 'Contrast' },
  { key: 'saturation', label: 'Saturation' },
  { key: 'warmth', label: 'Warmth' },
  { key: 'shadows', label: 'Shadows' },
  { key: 'highlights', label: 'Highlights' },
]

/** Preset + slider values (-100..100) → shader uniforms. */
export function filterUniforms(preset, adjust) {
  const p = preset.p
  const a = key => (adjust[key] || 0) / 100
  const mix = p.mix || [1, 1, 1, 0]
  const A = [(p.b || 0) + a('brightness') * 0.5, (p.k || 0) + a('contrast') * 0.5,
             (p.s || 0) + a('saturation'), (p.w || 0) + a('warmth')]
  const B = [(p.sh || 0) + a('shadows'), (p.hi || 0) + a('highlights'), p.t || 0, p.f || 0]
  const vig = p.v || 0
  const on = [...A, ...B, mix[3], vig].some(v => v !== 0) ? 1 : 0
  return { on, A, B, mix, vig }
}
