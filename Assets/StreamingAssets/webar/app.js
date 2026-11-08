// RealityDrop WebAR: 8th Wall XR8 SLAM + three.js, shown inside the Unity app's Android WebView.
// Placement follows the reference campaign: XR8 'responsive' scale keeps the ground at y = 0 from the
// first frame, and the placement point is a ray from the screen centre onto that ground — no waiting
// for plane detection. (Raw XR8 surface hit tests jumped metres frame to frame, so they're not used.)
import * as THREE from 'three'
import { GLTFLoader } from './lib/loaders/GLTFLoader.js'
import { DRACOLoader } from './lib/loaders/DRACOLoader.js'
import { RoomEnvironment } from './lib/environments/RoomEnvironment.js'
import { FILTER_FRAGMENT, PRESETS, ADJUSTMENTS, filterUniforms } from './filters.js'

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
  stickSpeed: 0.6,      // joystick: metres/second per metre of camera distance (clamped below)
  stickMin: 0.3, stickMax: 2.0,
  liftSpeed: 0.45,      // height buttons: metres/second
  maxHeight: 3,         // highest the model can be raised above the floor (metres)
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
  if (fxIsOpen()) { closeFx(); return }
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

// Placement reticle: neon ring + soft disc, lying on the ground
const reticle = new THREE.Group()
{
  const neon = new THREE.Color(0x3ef2ff)
  const ring = new THREE.Mesh(new THREE.RingGeometry(0.105, 0.125, 72),
    new THREE.MeshBasicMaterial({ color: neon, transparent: true, opacity: 0.95, depthWrite: false }))
  const disc = new THREE.Mesh(new THREE.CircleGeometry(0.105, 72),
    new THREE.MeshBasicMaterial({ color: neon, transparent: true, opacity: 0.14, depthWrite: false }))
  const dot = new THREE.Mesh(new THREE.CircleGeometry(0.012, 24),
    new THREE.MeshBasicMaterial({ color: 0xffffff, transparent: true, opacity: 0.9, depthWrite: false }))
  for (const m of [ring, disc, dot]) { m.rotation.x = -Math.PI / 2; m.renderOrder = 2; reticle.add(m) }
  reticle.visible = false
  scene.add(reticle)
}

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
  $('shutterRow').classList.toggle('hidden', next !== 'placed')
  $('fx').classList.toggle('hidden', next !== 'placed')
  if (next !== 'placed') closeFx()
  $('anim').classList.toggle('hidden', next !== 'placed' || !mixer)
  reticle.visible = next === 'ready'
  placed.visible = next === 'placed'
  if (next === 'scanning') hint('Move your phone slowly to scan the floor')
  else if (next === 'aim') hint('Point your phone at the floor')
  else if (next === 'ready') hint('Tap PLACE MODEL')
  else if (next === 'placed') hint('Joystick to move · ▲▼ height · Pinch / twist', 4500)
  if (next !== 'placed') releaseControls()
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
  popT = 0
  setState('placed')
  updateSizeLabel()
  native?.haptic?.()
  log('Placed at', targetPos.toArray().map(v => v.toFixed(2)).join(', '))
}
$('place').onclick = placeModel
$('reset').onclick = () => { setState(trackingOk ? 'aim' : 'scanning') }

function updateSizeLabel() {
  const h = targetPos.y
  $('size').textContent = `${Math.round(userScale * 100)}%` + (h > 0.01 ? ` · ${h < 1 ? Math.round(h * 100) + ' cm' : h.toFixed(2) + ' m'} up` : '')
}

// Subtle vibration on button taps (PLACE MODEL and the shutter already vibrate when they act)
document.addEventListener('click', e => {
  const button = e.target.closest('button')
  if (button && button.id !== 'place' && button.id !== 'shutter') native?.haptic?.()
})

// ------------------------------------------------------------------ gestures

const touchEl = $('touch')
let gesture = null
const dist = (a, b) => Math.hypot(a.clientX - b.clientX, a.clientY - b.clientY)
const angle = (a, b) => Math.atan2(b.clientY - a.clientY, b.clientX - a.clientX)

touchEl.addEventListener('touchstart', e => {
  e.preventDefault()
  const t = e.targetTouches
  if (t.length === 1) gesture = { kind: 'one', x: t[0].clientX, y: t[0].clientY, moved: false, last: screenToGround(t[0].clientX, t[0].clientY) }
  else if (t.length >= 2) gesture = { kind: 'two', d: dist(t[0], t[1]), a: angle(t[0], t[1]), scale: userScale, yaw: targetYaw }
}, { passive: false })

touchEl.addEventListener('touchmove', e => {
  e.preventDefault()
  if (!gesture || state !== 'placed') return
  const t = e.targetTouches
  if (gesture.kind === 'one' && t.length === 1) {
    if (Math.hypot(t[0].clientX - gesture.x, t[0].clientY - gesture.y) > 8) gesture.moved = true
    const hit = screenToGround(t[0].clientX, t[0].clientY)
    if (hit && gesture.last) targetPos.add(hit.clone().sub(gesture.last).setY(0)) // keeps the chosen height
    if (hit) gesture.last = hit
  } else if (gesture.kind === 'two' && t.length >= 2) {
    userScale = THREE.MathUtils.clamp(gesture.scale * dist(t[0], t[1]) / Math.max(1, gesture.d), TUNE.minScale, TUNE.maxScale)
    targetYaw = gesture.yaw - (angle(t[0], t[1]) - gesture.a)
    updateSizeLabel()
  }
}, { passive: false })

touchEl.addEventListener('touchend', e => {
  e.preventDefault()
  const t = e.targetTouches
  if (gesture?.kind === 'one' && !gesture.moved && t.length === 0 && state === 'ready') placeModel() // tap to place
  if (t.length === 0) gesture = null
  else if (t.length === 1) gesture = { kind: 'one', x: t[0].clientX, y: t[0].clientY, moved: true, last: screenToGround(t[0].clientX, t[0].clientY) }
}, { passive: false })

// ------------------------------------------------------------------ joystick (X/Z) + height buttons (Y)

const stickEl = $('stick'), knobEl = $('knob')
const stick = { x: 0, y: 0, id: null }   // -1..1, y = down on screen
let liftDir = 0
const STICK_RADIUS = 33                   // px the knob can travel from the centre

function setKnob(x, y) { knobEl.style.transform = `translate(${x * STICK_RADIUS}px, ${y * STICK_RADIUS}px)` }
function stickFrom(e) {
  const r = stickEl.getBoundingClientRect()
  let x = (e.clientX - (r.left + r.width / 2)) / STICK_RADIUS
  let y = (e.clientY - (r.top + r.height / 2)) / STICK_RADIUS
  const m = Math.hypot(x, y)
  if (m > 1) { x /= m; y /= m }
  stick.x = x; stick.y = y
  setKnob(x, y)
}
function stickEnd(e) {
  if (e && e.pointerId !== stick.id) return
  stick.id = null; stick.x = stick.y = 0
  stickEl.classList.remove('active')
  setKnob(0, 0)
}
stickEl.addEventListener('pointerdown', e => {
  if (stick.id !== null) return
  e.preventDefault()
  stick.id = e.pointerId
  stickEl.setPointerCapture(e.pointerId)
  stickEl.classList.add('active')
  stickFrom(e)
})
stickEl.addEventListener('pointermove', e => { if (e.pointerId === stick.id) stickFrom(e) })
for (const ev of ['pointerup', 'pointercancel', 'lostpointercapture']) stickEl.addEventListener(ev, stickEnd)

for (const [id, dir] of [['up', 1], ['down', -1]]) {
  const b = $(id)
  b.addEventListener('pointerdown', e => { e.preventDefault(); b.setPointerCapture(e.pointerId); liftDir = dir; b.classList.add('held') })
  const stop = () => { if (liftDir === dir) liftDir = 0; b.classList.remove('held') }
  for (const ev of ['pointerup', 'pointercancel', 'lostpointercapture']) b.addEventListener(ev, stop)
}

function releaseControls() {
  stickEnd(); liftDir = 0
  $('up').classList.remove('held'); $('down').classList.remove('held')
}

const fwd = new THREE.Vector3(), right = new THREE.Vector3()
function applyControls(dt) {
  const mag = Math.hypot(stick.x, stick.y)
  if (mag > 0.12) {                         // dead zone
    // Push up = away from you, right = to your right (camera-relative, flat on the floor)
    camera.getWorldDirection(fwd).setY(0)
    if (fwd.lengthSq() < 1e-6) fwd.set(0, 0, -1)
    fwd.normalize()
    right.crossVectors(fwd, camera.up).normalize()
    const dist = Math.hypot(targetPos.x - camera.position.x, targetPos.z - camera.position.z)
    const speed = THREE.MathUtils.clamp(dist * TUNE.stickSpeed, TUNE.stickMin, TUNE.stickMax)
    const k = speed * dt * Math.pow((mag - 0.12) / 0.88, 1.5) / mag // gentle near the centre for fine moves
    targetPos.addScaledVector(right, stick.x * k).addScaledVector(fwd, -stick.y * k)
  }
  if (liftDir) {
    const before = targetPos.y
    targetPos.y = THREE.MathUtils.clamp(targetPos.y + liftDir * TUNE.liftSpeed * dt, 0, TUNE.maxHeight)
    if (targetPos.y !== before) updateSizeLabel()
  }
}

// ------------------------------------------------------------------ camera filters (never touch the model)

let fxPreset = 0
const fxAdjust = {}
let fxGl = null, fxProgram = null, fxLoc = null, fxDirty = true

/** Sends new values to the camera shader; runs only when something changed, so idle frames cost nothing. */
function pushFilter(program) {
  if (!program || !fxGl) return
  if (program !== fxProgram) {
    fxProgram = program
    const at = n => fxGl.getUniformLocation(program, n)
    fxLoc = { on: at('uOn'), A: at('uA'), B: at('uB'), mix: at('uMix'), vig: at('uVig') }
    fxDirty = true
  }
  if (!fxDirty) return
  fxDirty = false
  const u = filterUniforms(PRESETS[fxPreset], fxAdjust)
  const previous = fxGl.getParameter(fxGl.CURRENT_PROGRAM)
  fxGl.useProgram(program)
  fxGl.uniform1f(fxLoc.on, u.on)
  fxGl.uniform4fv(fxLoc.A, u.A)
  fxGl.uniform4fv(fxLoc.B, u.B)
  fxGl.uniform4fv(fxLoc.mix, u.mix)
  fxGl.uniform1f(fxLoc.vig, u.vig)
  fxGl.useProgram(previous)
}

const fxIsOpen = () => !$('fxPanel').classList.contains('hidden')
function openFx() { $('fxPanel').classList.remove('hidden'); document.body.classList.add('fx-open') }
function closeFx() { $('fxPanel').classList.add('hidden'); document.body.classList.remove('fx-open') }

function showFxTab(adjust) {
  $('fxTabFilters').classList.toggle('on', !adjust)
  $('fxTabAdjust').classList.toggle('on', adjust)
  $('fxStrip').classList.toggle('hidden', adjust)
  $('fxSliders').classList.toggle('hidden', !adjust)
}

function selectPreset(i) {
  fxPreset = i
  fxDirty = true
  document.querySelectorAll('.fxPreset').forEach((b, n) => b.classList.toggle('on', n === i))
}

PRESETS.forEach((preset, i) => {
  const b = document.createElement('button')
  b.className = 'fxPreset'
  const swatch = document.createElement('i')
  swatch.style.background = preset.swatch
  b.append(swatch, preset.name)
  b.onclick = () => selectPreset(i)
  $('fxStrip').appendChild(b)
})
selectPreset(0)

ADJUSTMENTS.forEach(({ key, label }) => {
  const row = document.createElement('div')
  row.className = 'fxRow'
  const name = document.createElement('label')
  name.textContent = label
  const slider = document.createElement('input')
  Object.assign(slider, { type: 'range', min: -100, max: 100, value: 0 })
  slider.dataset.key = key
  const value = document.createElement('output')
  value.textContent = '0'
  slider.oninput = () => { fxAdjust[key] = +slider.value; value.textContent = slider.value; fxDirty = true }
  row.append(name, slider, value)
  $('fxSliders').appendChild(row)
})

$('fx').onclick = openFx
$('fxClose').onclick = closeFx
$('fxTabFilters').onclick = () => showFxTab(false)
$('fxTabAdjust').onclick = () => showFxTab(true)
$('fxReset').onclick = () => {
  for (const key of Object.keys(fxAdjust)) delete fxAdjust[key]
  document.querySelectorAll('.fxRow').forEach(row => {
    row.querySelector('input').value = 0
    row.querySelector('output').textContent = '0'
  })
  selectPreset(0)
}

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
      setState('ready')
      reticle.position.copy(p).setY(0.003)
      const s = 1 + 0.06 * Math.sin(performance.now() / 260)
      reticle.scale.setScalar(s)
    }
  }

  if (state === 'placed') {
    applyControls(dt)
    // Smooth follow (removes finger / pose jitter), pop-in on placement
    placed.position.lerp(targetPos, 1 - Math.exp(-dt * 14))
    let dy = targetYaw - placed.rotation.y
    placed.rotation.y += dy * (1 - Math.exp(-dt * 14))
    popT = Math.min(1, popT + dt / 0.35)
    const k = 0.6 + 0.4 * easeOutBack(popT) // quick pop-in
    const s = placed.scale.x + (userScale * k - placed.scale.x) * (popT < 1 ? 1 : 1 - Math.exp(-dt * 16))
    placed.scale.setScalar(Math.max(0.0001, s))
    // Contact shadow stays on the floor when the model is raised, and fades with height
    shadow.position.y = (0.002 - placed.position.y) / placed.scale.x
    shadow.material.opacity = THREE.MathUtils.clamp(1 - placed.position.y / 1.5, 0.15, 1)
  }

  if (mixer) mixer.update(dt)
}

// ------------------------------------------------------------------ XR8

function sizeCanvas() {
  const dpr = Math.min(window.devicePixelRatio || 1, 2) // full sharpness (as before)
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
    XR8.GlTextureRenderer.pipelineModule({ fragmentSource: FILTER_FRAGMENT }), // camera feed + filters
    XR8.XrController.pipelineModule(),        // SLAM
    {
      name: 'realitydrop',
      onStart: ({ canvas: c, GLctx }) => {
        fxGl = GLctx
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
      onUpdate: ({ processCpuResult, processGpuResult }) => {
        pushFilter(processGpuResult?.gltexturerenderer?.shader)
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
        countFrame()
        if (captureRequested) takePhoto() // read the frame right after drawing it
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

// ------------------------------------------------------------------ frame rate

let frames = 0, fps = 0
function countFrame() { frames++ }
setInterval(() => {                      // every 2 s: FPS + tracking log (logcat tag RealityDropWebAR)
  fps = frames / 2; frames = 0
  if (state === 'starting') return
  log(`fps=${fps.toFixed(0)} render=${canvas.width}x${canvas.height} state=${state} tracking=${trackingStatus} travel=${travel.toFixed(2)}m`)
}, 2000)

// ------------------------------------------------------------------ photo capture

let captureRequested = false
function requestPhoto() {
  if (captureRequested || !renderer) return
  captureRequested = true
  if (params.has('preview')) takePhoto()
}
function takePhoto() {
  captureRequested = false
  let data
  try { data = canvas.toDataURL('image/jpeg', 0.92) } catch (e) { log('Capture failed', e); toast("Couldn't take the photo"); return }
  // Shutter flash + thumbnail (the HTML buttons are not part of the picture)
  const flash = $('flash')
  flash.classList.remove('go'); void flash.offsetWidth; flash.classList.add('go')
  native?.haptic?.()
  $('photoThumb').src = data
  $('photoText').textContent = 'Saving…'
  $('share').classList.add('hidden')
  showPhotoCard()
  const base64 = data.slice(data.indexOf(',') + 1)
  if (native?.savePhoto) setTimeout(() => native.savePhoto(base64), 30) // let the flash start first
  else window.rdPhotoSaved(false, false)
}
let photoTimer = 0
function showPhotoCard() {
  const card = $('photo')
  card.classList.remove('hidden', 'out')
  clearTimeout(photoTimer)
  photoTimer = setTimeout(() => card.classList.add('out'), 4500)
}
window.rdPhotoSaved = (ok, canShare) => {
  $('photoText').textContent = ok ? 'Saved to Gallery' : (native ? "Couldn't save the photo" : 'Preview only')
  $('share').classList.toggle('hidden', !(ok && canShare))
  showPhotoCard()
}
function toast(text) { hint(text, 2500) }
$('shutter').onclick = requestPhoto
$('share').onclick = () => native?.sharePhoto?.()

/** Desktop check without camera/XR8 (?preview=1): fixed camera looking at the floor. */
function startPreview() {
  sizeCanvas()
  renderer = new THREE.WebGLRenderer({ canvas, antialias: true, preserveDrawingBuffer: true })
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
