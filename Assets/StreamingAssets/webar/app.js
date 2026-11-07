// RealityDrop WebAR: 8th Wall XR8 SLAM + three.js, shown inside the Unity app's Android WebView.
// Placement follows the reference campaign: XR8 'responsive' scale keeps the ground at y = 0 from the
// first frame, and the placement point is a ray from the screen centre onto that ground — no waiting
// for plane detection. (Raw XR8 surface hit tests jumped metres frame to frame, so they're not used.)
import * as THREE from 'three'
import { GLTFLoader } from './lib/loaders/GLTFLoader.js'
import { DRACOLoader } from './lib/loaders/DRACOLoader.js'
import { RoomEnvironment } from './lib/environments/RoomEnvironment.js'

const params = new URLSearchParams(location.search)
const MODEL_URL = params.get('model') || '/model.glb'
const TITLE = params.get('title') || '3D model'

const TUNE = {
  minTravel: 0.08,      // metres the phone must move before SLAM is trusted
  minDownward: 0.12,    // camera must look at least this much down (sine) to aim at the floor
  minDistance: 0.3, maxDistance: 6,
  groundSmoothing: 0.12, groundSnap: 0.35,
  targetSize: 0.6,      // largest model dimension on first placement (metres)
  minScale: 0.1, maxScale: 10,
}

const $ = id => document.getElementById(id)
const native = window.RealityDropNative
const log = (...a) => console.log('[WebAR]', ...a)

// ------------------------------------------------------------------ UI

$('title').textContent = TITLE
let hintTimer = 0
function hint(text, autoHideMs = 0) {
  const el = $('hint')
  el.textContent = text
  el.classList.toggle('hidden', !text)
  el.classList.remove('fade')
  clearTimeout(hintTimer)
  if (autoHideMs) hintTimer = setTimeout(() => el.classList.add('fade'), autoHideMs)
}
let failed = false
function fail(message) {
  if (failed) return
  failed = true
  log('FAILED:', message)
  $('errorText').textContent = message
  $('error').classList.remove('hidden')
  $('ring').classList.add('hidden')
  $('place').classList.add('hidden')
  reticle.visible = false
  hint('')
}
function goBack() {
  try { window.XR8?.stop() } catch { /* already stopped */ }
  if (native?.close) native.close()
  else history.back()
}
window.rdBack = () => {                     // Android back key (called from native)
  if (!$('license').classList.contains('hidden')) { $('license').classList.add('hidden'); return }
  goBack()
}
$('back').onclick = goBack
$('errorBack').onclick = goBack
$('credit').onclick = async () => {
  $('license').classList.remove('hidden')
  try { $('licenseText').textContent = await (await fetch('xr/LICENSE')).text() }
  catch { $('licenseText').textContent = 'License text unavailable.' }
}
$('licenseClose').onclick = () => $('license').classList.add('hidden')

// ------------------------------------------------------------------ three.js scene

const canvas = $('xr')
const scene = new THREE.Scene()
const camera = new THREE.PerspectiveCamera(60, innerWidth / innerHeight, 0.01, 200)
scene.add(new THREE.HemisphereLight(0xffffff, 0x404858, 1.1))
const sun = new THREE.DirectionalLight(0xffffff, 1.6)
sun.position.set(1.2, 3, 1.6)
scene.add(sun)

// Soft round glow (canvas texture) used by the reticle halo and the pixel dust
function glowTexture(inner = 0.0, stops = [[0, 1], [0.35, 0.55], [1, 0]]) {
  const c = document.createElement('canvas'); c.width = c.height = 128
  const g = c.getContext('2d')
  const grad = g.createRadialGradient(64, 64, 64 * inner, 64, 64, 64)
  for (const [at, a] of stops) grad.addColorStop(at, `rgba(255,255,255,${a})`)
  g.fillStyle = grad; g.fillRect(0, 0, 128, 128)
  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace
  return t
}

// Placement reticle (bigger, glowing): halo + disc + solid ring + rotating dashed arc + centre dot.
// Its size follows the distance (see frame): far away it grows so it stays easy to see, close up it shrinks.
const NEON = new THREE.Color(0x3ef2ff)
const RETICLE_RADIUS = 0.2 // metres at 1.5 m distance
const reticle = new THREE.Group()
const reticleSpin = new THREE.Group()
let reticleScale = 1
{
  const add = (mesh, order = 2) => { mesh.rotation.x = -Math.PI / 2; mesh.renderOrder = order; return mesh }
  const basic = (opts) => new THREE.MeshBasicMaterial({ color: NEON, transparent: true, depthWrite: false, ...opts })
  const r = RETICLE_RADIUS
  const halo = add(new THREE.Mesh(new THREE.PlaneGeometry(r * 3.2, r * 3.2),
    basic({ map: glowTexture(0.45, [[0, 0], [0.55, 0.55], [0.75, 0.18], [1, 0]]), blending: THREE.AdditiveBlending, opacity: 0.9 })), 1)
  const disc = add(new THREE.Mesh(new THREE.CircleGeometry(r * 0.9, 96), basic({ opacity: 0.12 })))
  const ring = add(new THREE.Mesh(new THREE.RingGeometry(r * 0.9, r, 96), basic({ opacity: 0.95 })))
  const dot = add(new THREE.Mesh(new THREE.CircleGeometry(r * 0.07, 32), basic({ color: 0xffffff, opacity: 0.95 })))
  // Dashed outer arc that slowly rotates
  for (let i = 0; i < 6; i++) {
    const a0 = i / 6 * Math.PI * 2
    reticleSpin.add(add(new THREE.Mesh(new THREE.RingGeometry(r * 1.1, r * 1.16, 24, 1, a0, Math.PI / 6), basic({ opacity: 0.8 }))))
  }
  reticle.add(halo, disc, ring, dot, reticleSpin)
  reticle.visible = false
  scene.add(reticle)
}

// ------------------------------------------------------------------ placement FX: spiral pixel dust

const DUST_COUNT = 360
const DUST_TIME = 1.8 // seconds
const dust = (() => {
  const geo = new THREE.BufferGeometry()
  geo.setAttribute('position', new THREE.BufferAttribute(new Float32Array(DUST_COUNT * 3), 3))
  geo.setAttribute('aAlpha', new THREE.BufferAttribute(new Float32Array(DUST_COUNT), 1))
  geo.setAttribute('aSize', new THREE.BufferAttribute(new Float32Array(DUST_COUNT), 1))
  const mat = new THREE.ShaderMaterial({
    transparent: true, depthWrite: false, blending: THREE.AdditiveBlending,
    uniforms: { uMap: { value: glowTexture() }, uColor: { value: new THREE.Color(0x38c8ff) }, uScale: { value: 800 } },
    vertexShader: `
      attribute float aAlpha; attribute float aSize; varying float vAlpha; uniform float uScale;
      void main() {
        vAlpha = aAlpha;
        vec4 mv = modelViewMatrix * vec4(position, 1.0);
        gl_PointSize = aSize * uScale / max(0.05, -mv.z);
        gl_Position = projectionMatrix * mv;
      }`,
    fragmentShader: `
      uniform sampler2D uMap; uniform vec3 uColor; varying float vAlpha;
      void main() {
        float a = texture2D(uMap, gl_PointCoord).a * vAlpha;
        if (a < 0.01) discard;
        vec3 core = mix(uColor, vec3(1.0), smoothstep(0.6, 1.0, a)); // white-hot centres
        gl_FragColor = vec4(core * a * 1.6, a);
      }`,
  })
  const points = new THREE.Points(geo, mat)
  points.frustumCulled = false
  points.visible = false
  points.renderOrder = 3
  scene.add(points)

  // Shockwave ring on the ground
  const wave = new THREE.Mesh(new THREE.RingGeometry(0.965, 1, 128),
    new THREE.MeshBasicMaterial({ color: NEON, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending }))
  wave.rotation.x = -Math.PI / 2
  wave.visible = false
  scene.add(wave)

  const seeds = []
  let t = -1, height = 0.6, radius = 0.3
  return {
    burst(at, modelHeight, modelRadius) {
      height = Math.max(0.25, modelHeight * 1.25)
      radius = Math.max(0.15, modelRadius * 1.15)
      seeds.length = 0
      for (let i = 0; i < DUST_COUNT; i++) {
        seeds.push({
          a: Math.random() * Math.PI * 2,              // start angle
          r: radius * (0.55 + Math.random() * 0.6),    // orbit radius
          h: 0.15 + Math.random() * 0.85,              // how high it climbs (fraction)
          spin: (1.2 + Math.random() * 1.3) * (Math.random() < 0.85 ? 1 : -1), // turns over the effect
          delay: Math.random() * 0.35,
          size: 0.035 + Math.random() * 0.05,
          twinkle: Math.random() * 6.28,
        })
      }
      points.position.copy(at)
      wave.position.copy(at).setY(0.004)
      points.visible = wave.visible = true
      t = 0
    },
    update(dt) {
      if (t < 0) return
      t += dt
      const pos = geo.attributes.position.array, alpha = geo.attributes.aAlpha.array, size = geo.attributes.aSize.array
      for (let i = 0; i < DUST_COUNT; i++) {
        const s = seeds[i]
        const k = THREE.MathUtils.clamp((t - s.delay) / (DUST_TIME - s.delay), 0, 1)
        const rise = 1 - (1 - k) ** 2.2                       // fast start, eases out
        const ang = s.a + s.spin * Math.PI * 2 * rise
        const r = s.r * (1 - 0.65 * rise)                      // spiral inwards while rising
        pos[i * 3] = Math.cos(ang) * r
        pos[i * 3 + 1] = s.h * height * rise
        pos[i * 3 + 2] = Math.sin(ang) * r
        const fade = k <= 0 ? 0 : Math.min(1, k * 6) * (1 - k) ** 1.3
        alpha[i] = Math.min(1, fade * 1.4) * (0.7 + 0.3 * Math.sin(t * 18 + s.twinkle))
        size[i] = s.size * (1.2 - 0.5 * k)
      }
      geo.attributes.position.needsUpdate = geo.attributes.aAlpha.needsUpdate = geo.attributes.aSize.needsUpdate = true
      mat.uniforms.uScale.value = (renderer ? renderer.domElement.height : innerHeight) * 0.9

      const w = Math.min(1, t / 0.7)                            // shockwave: 0.7 s
      wave.scale.setScalar(0.05 + radius * 1.5 * (1 - (1 - w) ** 3))
      wave.material.opacity = 0.9 * (1 - w)
      wave.visible = w < 1

      if (t >= DUST_TIME) { t = -1; points.visible = wave.visible = false }
    },
    stop() { t = -1; points.visible = wave.visible = false },
  }
})()

// Soft contact shadow under the model
function shadowTexture() {
  const c = document.createElement('canvas'); c.width = c.height = 128
  const g = c.getContext('2d')
  const grad = g.createRadialGradient(64, 64, 0, 64, 64, 64)
  grad.addColorStop(0, 'rgba(0,0,0,0.55)'); grad.addColorStop(0.55, 'rgba(0,0,0,0.25)'); grad.addColorStop(1, 'rgba(0,0,0,0)')
  g.fillStyle = grad; g.fillRect(0, 0, 128, 128)
  return new THREE.CanvasTexture(c)
}
const shadow = new THREE.Mesh(new THREE.PlaneGeometry(1, 1),
  new THREE.MeshBasicMaterial({ map: shadowTexture(), transparent: true, depthWrite: false }))
shadow.rotation.x = -Math.PI / 2
shadow.position.y = 0.002

// placed (position / yaw / user scale) → fitted (auto size) → gltf scene
const placed = new THREE.Group()
const fitted = new THREE.Group()
placed.add(shadow, fitted)
placed.visible = false
scene.add(placed)

let mixer = null, actions = [], current = null, paused = false
let modelReady = false
let fittedHeight = 0.6, fittedRadius = 0.3 // model size at 100% (metres), for the placement effect

// ------------------------------------------------------------------ model

function loadModel() {
  const loader = new GLTFLoader()
  const draco = new DRACOLoader()
  draco.setDecoderPath('./draco/')
  loader.setDRACOLoader(draco)
  const t0 = performance.now()
  loader.load(MODEL_URL, gltf => {
    const root = gltf.scene
    root.traverse(o => {
      if (o.isSkinnedMesh) o.frustumCulled = false // animated meshes can move outside their bind box
    })
    // Fit: largest dimension = targetSize, base centred on the origin, sitting on the ground
    root.updateMatrixWorld(true)
    const box = new THREE.Box3().setFromObject(root)
    const size = box.getSize(new THREE.Vector3())
    const centre = box.getCenter(new THREE.Vector3())
    const maxDim = Math.max(size.x, size.y, size.z) || 1
    root.position.set(-centre.x, -box.min.y, -centre.z)
    fitted.scale.setScalar(TUNE.targetSize / maxDim)
    fitted.add(root)
    const footprint = Math.max(size.x, size.z) / maxDim * TUNE.targetSize
    shadow.scale.setScalar(Math.max(0.15, footprint * 1.35))
    fittedHeight = size.y / maxDim * TUNE.targetSize
    fittedRadius = footprint / 2

    if (gltf.animations?.length) setupAnimations(root, gltf.animations)
    modelReady = true
    log(`Model loaded in ${((performance.now() - t0) / 1000).toFixed(2)}s, ${gltf.animations?.length || 0} clip(s).`)
    refreshPlaceButton()
  }, e => {
    if (e.total) $('place').textContent = `LOADING ${Math.round(e.loaded / e.total * 100)}%`
  }, err => {
    log('Model load error', err)
    fail("This model couldn't be opened in AR. Please go back and try another one.")
  })
}

function setupAnimations(root, clips) {
  mixer = new THREE.AnimationMixer(root)
  actions = clips.map(c => mixer.clipAction(c))
  const select = $('clips')
  clips.forEach((c, i) => {
    const opt = document.createElement('option')
    opt.value = i
    opt.textContent = prettyClip(c.name, i)
    select.appendChild(opt)
  })
  $('chev').classList.toggle('hidden', clips.length < 2)
  select.disabled = clips.length < 2
  select.style.pointerEvents = clips.length < 2 ? 'none' : ''
  select.onchange = () => playClip(+select.value)
  $('play').onclick = togglePause
  $('clipWrap').onclick = e => { if (clips.length < 2) { e.preventDefault(); togglePause() } }
  playClip(0)
}
function prettyClip(name, i) {
  if (!name || /^animation_?\d*$/i.test(name)) return `Animation ${i + 1}`
  return name.replace(/_/g, ' ')
}
function playClip(i) {
  const next = actions[i]
  if (!next) return
  next.reset().setLoop(THREE.LoopRepeat, Infinity).play()
  if (current && current !== next) current.crossFadeTo(next, 0.25, false)
  current = next
  paused = false
  mixer.timeScale = 1
  $('clipName').textContent = prettyClip(next.getClip().name, i)
  $('clips').value = i
  syncPlayIcon()
}
function togglePause() {
  if (!mixer) return
  paused = !paused
  mixer.timeScale = paused ? 0 : 1
  syncPlayIcon()
}
function syncPlayIcon() {
  $('playIcon').classList.toggle('hidden', !paused)
  $('pauseIcon').classList.toggle('hidden', paused)
}

// ------------------------------------------------------------------ tracking + placement state

let state = 'starting'        // starting → scanning → aim → ready → placed
let renderer = null
let lastPose = -Infinity
let travel = 0, origin = null, trackingOk = false, trackingStatus = 'UNKNOWN'
const ground = new THREE.Vector3()
let hasGround = false
let userScale = 1, targetYaw = 0
const targetPos = new THREE.Vector3()
let popT = 1

function refreshPlaceButton() {
  const btn = $('place')
  if (state === 'placed') { btn.classList.add('hidden'); return }
  btn.classList.remove('hidden')
  if (!modelReady) { btn.disabled = true; if (!btn.textContent.startsWith('LOADING')) btn.textContent = 'LOADING…'; return }
  btn.textContent = 'PLACE MODEL'
  btn.disabled = state !== 'ready'
}

function setState(next) {
  if (state === next) return
  state = next
  $('ring').classList.toggle('hidden', next === 'ready' || next === 'placed')
  $('reset').classList.toggle('hidden', next !== 'placed')
  $('size').classList.toggle('hidden', next !== 'placed')
  $('anim').classList.toggle('hidden', next !== 'placed' || !mixer)
  reticle.visible = next === 'ready'
  placed.visible = next === 'placed'
  if (next === 'scanning') hint('Move your phone slowly to scan the floor')
  else if (next === 'aim') hint('Point your phone at the floor')
  else if (next === 'ready') hint('Tap PLACE MODEL')
  else if (next === 'placed') hint('Drag to move · Pinch to resize · Twist to rotate', 4000)
  refreshPlaceButton()
}

function updateTracking(reality) {
  trackingStatus = reality.trackingStatus || 'UNKNOWN'
  const at = new THREE.Vector3(reality.position.x, reality.position.y, reality.position.z)
  // Largest displacement from the first pose (summing per-frame steps would count sensor noise)
  if (!origin) origin = at
  else travel = Math.max(travel, at.distanceTo(origin))
  trackingOk = trackingStatus === 'NORMAL' && travel >= TUNE.minTravel
}

/** Ray from the screen centre onto the ground plane (y = 0), smoothed. */
function centreGround() {
  if (!trackingOk || performance.now() - lastPose > 250) { hasGround = false; return null }
  const dir = camera.getWorldDirection(new THREE.Vector3())
  if (dir.y > -TUNE.minDownward) { hasGround = false; return null }
  const t = -camera.position.y / dir.y
  if (t <= 0) return null
  const p = camera.position.clone().addScaledVector(dir, t)
  const d = camera.position.distanceTo(p)
  if (d < TUNE.minDistance || d > TUNE.maxDistance) { hasGround = false; return null }
  if (!hasGround || ground.distanceTo(p) > TUNE.groundSnap) { ground.copy(p); hasGround = true }
  else ground.lerp(p, TUNE.groundSmoothing)
  return ground
}

const raycaster = new THREE.Raycaster()
const groundPlane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0)
/** Where a screen point (client px) meets the ground, or null. */
function screenToGround(x, y) {
  const ndc = new THREE.Vector2(x / innerWidth * 2 - 1, -(y / innerHeight) * 2 + 1)
  raycaster.setFromCamera(ndc, camera)
  const hit = raycaster.ray.intersectPlane(groundPlane, new THREE.Vector3())
  if (!hit || hit.distanceTo(camera.position) > TUNE.maxDistance * 1.5) return null
  return hit
}

function placeModel() {
  if (state !== 'ready' || !modelReady) return
  targetPos.copy(ground).setY(0)
  placed.position.copy(targetPos)
  targetYaw = Math.atan2(camera.position.x - targetPos.x, camera.position.z - targetPos.z) // face the user
  placed.rotation.y = targetYaw
  userScale = 1
  popT = -0.45 // the dust swirls first; the model materialises inside it
  placed.scale.setScalar(0.0001)
  dust.burst(targetPos, fittedHeight, fittedRadius)
  setState('placed')
  updateSizeLabel()
  native?.haptic?.()
  log('Placed at', targetPos.toArray().map(v => v.toFixed(2)).join(', '))
}
$('place').onclick = placeModel
$('reset').onclick = () => { dust.stop(); setState(trackingOk ? 'aim' : 'scanning') }

function updateSizeLabel() { $('size').textContent = `${Math.round(userScale * 100)}%` }

// ------------------------------------------------------------------ gestures

const touchEl = $('touch')
let gesture = null
const dist = (a, b) => Math.hypot(a.clientX - b.clientX, a.clientY - b.clientY)
const angle = (a, b) => Math.atan2(b.clientY - a.clientY, b.clientX - a.clientX)

touchEl.addEventListener('touchstart', e => {
  e.preventDefault()
  const t = e.touches
  if (t.length === 1) gesture = { kind: 'one', x: t[0].clientX, y: t[0].clientY, moved: false, last: screenToGround(t[0].clientX, t[0].clientY) }
  else if (t.length >= 2) gesture = { kind: 'two', d: dist(t[0], t[1]), a: angle(t[0], t[1]), scale: userScale, yaw: targetYaw }
}, { passive: false })

touchEl.addEventListener('touchmove', e => {
  e.preventDefault()
  if (!gesture || state !== 'placed') return
  const t = e.touches
  if (gesture.kind === 'one' && t.length === 1) {
    if (Math.hypot(t[0].clientX - gesture.x, t[0].clientY - gesture.y) > 8) gesture.moved = true
    const hit = screenToGround(t[0].clientX, t[0].clientY)
    if (hit && gesture.last) targetPos.add(hit.clone().sub(gesture.last)).setY(0)
    if (hit) gesture.last = hit
  } else if (gesture.kind === 'two' && t.length >= 2) {
    userScale = THREE.MathUtils.clamp(gesture.scale * dist(t[0], t[1]) / Math.max(1, gesture.d), TUNE.minScale, TUNE.maxScale)
    targetYaw = gesture.yaw - (angle(t[0], t[1]) - gesture.a)
    updateSizeLabel()
  }
}, { passive: false })

touchEl.addEventListener('touchend', e => {
  e.preventDefault()
  if (gesture?.kind === 'one' && !gesture.moved && e.touches.length === 0 && state === 'ready') placeModel() // tap to place
  if (e.touches.length === 0) gesture = null
  else if (e.touches.length === 1) gesture = { kind: 'one', x: e.touches[0].clientX, y: e.touches[0].clientY, moved: true, last: screenToGround(e.touches[0].clientX, e.touches[0].clientY) }
}, { passive: false })

// ------------------------------------------------------------------ per-frame

const clock = new THREE.Clock()
const easeOutBack = t => { const c = 1.70158; return 1 + (c + 1) * (t - 1) ** 3 + c * (t - 1) ** 2 }

function frame() {
  const dt = Math.min(clock.getDelta(), 0.1)

  if (state !== 'placed' && state !== 'starting') {
    const p = centreGround()
    if (!trackingOk) setState('scanning')
    else if (!p) setState('aim')
    else {
      const wasReady = state === 'ready'
      setState('ready')
      reticle.position.copy(p).setY(0.003)
      // Distance-aware size: ~1x at 1.5 m, bigger further away, smaller up close (smoothed)
      const d = camera.position.distanceTo(p)
      const want = THREE.MathUtils.clamp(d / 1.5, 0.55, 2.6)
      reticleScale = wasReady ? reticleScale + (want - reticleScale) * (1 - Math.exp(-dt * 8)) : want
      const breathe = 1 + 0.05 * Math.sin(performance.now() / 260)
      reticle.scale.setScalar(reticleScale * breathe)
      reticleSpin.rotation.y += dt * 0.9
    }
  }

  if (state === 'placed') {
    // Smooth follow (removes finger / pose jitter), pop-in on placement
    placed.position.lerp(targetPos, 1 - Math.exp(-dt * 14))
    let dy = targetYaw - placed.rotation.y
    placed.rotation.y += dy * (1 - Math.exp(-dt * 14))
    popT = Math.min(1, popT + dt / 0.5)
    const k = popT <= 0 ? 0.0001 : 0.2 + 0.8 * easeOutBack(popT) // grows out of the dust
    const s = placed.scale.x + (userScale * k - placed.scale.x) * (popT < 1 ? 1 : 1 - Math.exp(-dt * 16))
    placed.scale.setScalar(Math.max(0.0001, s))
  }

  dust.update(dt)
  if (mixer) mixer.update(dt)
}

// ------------------------------------------------------------------ XR8

function sizeCanvas() {
  const dpr = Math.min(window.devicePixelRatio || 1, 2)
  canvas.width = Math.round(innerWidth * dpr)
  canvas.height = Math.round(innerHeight * dpr)
}

// Android never needs a motion-sensor prompt, but newer Chromium exposes the iOS-style
// requestPermission(), which answers "denied" inside an app WebView (no prompt UI) and
// XR8 then refuses to start. Motion sensors need no permission on Android, so report granted.
if (!/iPhone|iPad|iPod/i.test(navigator.userAgent)) {
  for (const E of [window.DeviceOrientationEvent, window.DeviceMotionEvent]) {
    if (E && typeof E.requestPermission === 'function') E.requestPermission = () => Promise.resolve('granted')
  }
}

function loadXR8() {
  return new Promise((resolve, reject) => {
    if (window.XR8) return resolve(window.XR8)
    const timer = setTimeout(() => reject(new Error('The AR engine took too long to load.')), 25000)
    window.addEventListener('xrloaded', () => { clearTimeout(timer); resolve(window.XR8) }, { once: true })
    const s = document.createElement('script')
    s.src = './xr/xr.js'
    s.async = true
    s.dataset.preloadChunks = 'slam'
    s.onerror = () => { clearTimeout(timer); reject(new Error('The AR engine could not be loaded.')) }
    document.head.appendChild(s)
  })
}

function syncControllerCamera() {
  window.XR8?.XrController.updateCameraProjectionMatrix({
    cam: { pixelRectWidth: canvas.width, pixelRectHeight: canvas.height, nearClipPlane: camera.near, farClipPlane: camera.far },
  })
}

async function startAR() {
  sizeCanvas()
  hint('Starting camera…')
  const XR8 = await loadXR8()
  if (!XR8.XrController && XR8.loadChunk) await XR8.loadChunk('slam')
  log('XR8 loaded')

  XR8.XrController.configure({ scale: 'responsive' })
  XR8.addCameraPipelineModules([
    XR8.GlTextureRenderer.pipelineModule(),   // camera feed
    XR8.XrController.pipelineModule(),        // SLAM
    {
      name: 'realitydrop',
      onStart: ({ canvas: c, GLctx }) => {
        renderer = new THREE.WebGLRenderer({ canvas: c, context: GLctx, alpha: true, antialias: true })
        renderer.autoClear = false
        renderer.setPixelRatio(1)
        renderer.setSize(c.width, c.height, false)
        renderer.outputColorSpace = THREE.SRGBColorSpace
        renderer.toneMapping = THREE.NeutralToneMapping
        const pmrem = new THREE.PMREMGenerator(renderer)
        scene.environment = pmrem.fromScene(new RoomEnvironment(), 0.04).texture
        pmrem.dispose()
        syncControllerCamera()
        log('AR started', c.width + 'x' + c.height)
        state = 'starting'
        setState('scanning')
      },
      onUpdate: ({ processCpuResult }) => {
        const reality = processCpuResult?.reality
        if (!reality?.position || !reality.rotation || !reality.intrinsics) return
        updateTracking(reality)
        camera.position.set(reality.position.x, reality.position.y, reality.position.z)
        camera.quaternion.set(reality.rotation.x, reality.rotation.y, reality.rotation.z, reality.rotation.w)
        camera.projectionMatrix.fromArray(reality.intrinsics)
        camera.projectionMatrixInverse.copy(camera.projectionMatrix).invert()
        camera.updateMatrixWorld()
        lastPose = performance.now()
      },
      onRender: () => {
        if (!renderer) return
        frame()
        renderer.resetState() // XR8 changes GL state behind three's cache every frame
        renderer.clearDepth()
        renderer.render(scene, camera)
      },
      onCanvasSizeChange: () => {
        if (renderer) renderer.setSize(canvas.width, canvas.height, false)
        syncControllerCamera()
      },
      onCameraStatusChange: (event) => {
        const { status } = event
        log('Camera status:', status, status === 'failed' ? JSON.stringify({ reason: event.reason, error: String(event.error || '') }) : '')
        if (status === 'requesting') hint('Allow camera access…')
        if (status === 'failed') fail('Camera access was blocked. Allow camera permission for RealityDrop in Settings and try again.')
      },
      onException: err => {
        log('XR8 exception', err)
        fail('The AR camera stopped unexpectedly. Please go back and try again.')
      },
    },
  ])
  XR8.run({ canvas, webgl2: true })
}

addEventListener('resize', sizeCanvas)
setInterval(() => {                      // tracking-quality log for logcat (every 2 s)
  if (state !== 'starting') log(`state=${state} tracking=${trackingStatus} travel=${travel.toFixed(2)}m cam.y=${camera.position.y.toFixed(2)}`)
}, 2000)

/** Desktop check without camera/XR8 (?preview=1): fixed camera looking at the floor. */
function startPreview() {
  sizeCanvas()
  renderer = new THREE.WebGLRenderer({ canvas, antialias: true })
  renderer.setPixelRatio(1)
  renderer.setSize(canvas.width, canvas.height, false)
  renderer.outputColorSpace = THREE.SRGBColorSpace
  renderer.toneMapping = THREE.NeutralToneMapping
  renderer.setClearColor(0x1b2330)
  const pmrem = new THREE.PMREMGenerator(renderer)
  scene.environment = pmrem.fromScene(new RoomEnvironment(), 0.04).texture
  scene.add(new THREE.GridHelper(10, 40, 0x335066, 0x24384a))
  camera.aspect = canvas.width / canvas.height
  camera.position.set(0, 1.35, 1.6)
  camera.lookAt(0, 0, -0.1)
  camera.updateProjectionMatrix()
  trackingOk = true; trackingStatus = 'NORMAL'
  state = 'starting'; setState('scanning')
  const loop = () => {
    lastPose = performance.now()
    camera.updateMatrixWorld()
    frame()
    renderer.render(scene, camera)
    requestAnimationFrame(loop)
  }
  loop()
}

loadModel()
refreshPlaceButton()
if (params.has('preview')) startPreview()
else startAR().catch(err => fail(err?.message || String(err)))
