// TEST ONLY (not shipped; used by Tools/WebTest/padtest.sh): a stand-in Xbox 360 controller for
// headless Chrome, which has no real one. ?fakepad=<kind> picks how the browser reports it:
//   standard       Chrome / Edge on Windows (W3C standard mapping)
//   linux          Firefox on Linux, wired pad (xpad: 11 buttons, 8 axes)
//   linuxwireless  Firefox on Linux, wireless receiver (xpad, D-pad as buttons: 15 buttons, 6 axes)
//   thirdparty     Firefox on Linux, a third-party XInput pad (Logitech F310, xpad layout)
//   mac            Firefox on macOS, the 360Controller driver (15 buttons, 6 axes)
// For the raw kinds it also makes the page think it is on Linux or macOS. Once the game logs
// "[PadTest] ready", it connects, then presses each control in turn.
(function () {
  "use strict";
  var kind = (location.search.match(/fakepad=(\w+)/) || [])[1] || "standard";
  var os = { standard: "Win32", linux: "Linux x86_64", linuxwireless: "Linux x86_64", thirdparty: "Linux x86_64", mac: "MacIntel" }[kind];
  if (!os) { console.log("[FakePad] unknown kind " + kind); return; }
  Object.defineProperty(navigator, "platform", { get: function () { return os; } });
  Object.defineProperty(navigator, "userAgentData", { get: function () { return { platform: os.indexOf("Linux") === 0 ? "Linux" : os === "MacIntel" ? "macOS" : "Windows" }; } });

  var shapes = {   // buttons, axes, id
    standard: [17, 4, "Xbox 360 Controller (XInput STANDARD GAMEPAD)"],
    linux: [11, 8, "045e-028e-Microsoft X-Box 360 pad"],
    linuxwireless: [15, 6, "045e-0719-Xbox 360 Wireless Receiver"],
    thirdparty: [11, 8, "046d-c21d-Logitech Gamepad F310"],
    mac: [15, 6, "45e-28e-Xbox 360 Wired Controller"]
  };
  var nb = shapes[kind][0], na = shapes[kind][1];
  var buttons = [], axes = [];
  for (var i = 0; i < nb; i++) buttons.push({ pressed: false, touched: false, value: 0 });
  for (var j = 0; j < na; j++) axes.push(0);   // raw trigger axes read 0 until first moved
  var pad = {
    id: shapes[kind][2], index: 0, connected: true, mapping: kind === "standard" ? "standard" : "",
    timestamp: performance.now(), buttons: buttons, axes: axes,
    vibrationActuator: {
      type: "dual-rumble",
      playEffect: function (t, p) { console.log("[FakePad] rumble " + t + " strong " + p.strongMagnitude.toFixed(2) + " weak " + p.weakMagnitude.toFixed(2) + " " + p.duration + " ms"); return Promise.resolve("complete"); },
      reset: function () { console.log("[FakePad] rumble stop"); return Promise.resolve("complete"); }
    }
  };
  navigator.getGamepads = function () { return [pad, null, null, null]; };

  function touch() { pad.timestamp = performance.now(); }
  function setButton(i, v) { buttons[i] = { pressed: v > 0.5, touched: v > 0, value: v }; touch(); }
  function setAxis(i, v) { axes[i] = v; touch(); }

  // what each named control is on this layout: ["b", button] or ["a", axis, pressed value, released value]
  var sticks = { LSright: ["a", 0, 1, 0], LSup: ["a", 1, -1, 0], RSleft: ["a", 3, -1, 0], RSdown: ["a", 4, 1, 0] };
  var trig = { LT: ["a", 2, 1, -1], RT: ["a", 5, 1, -1] };
  function merge() { var o = {}; for (var k = 0; k < arguments.length; k++) for (var n in arguments[k]) o[n] = arguments[k][n]; return o; }
  var face = { A: ["b", 0], B: ["b", 1], X: ["b", 2], Y: ["b", 3], LB: ["b", 4], RB: ["b", 5] };
  var L = {
    standard: merge(face, {
      LT: ["b", 6], RT: ["b", 7], Back: ["b", 8], Start: ["b", 9], L3: ["b", 10], R3: ["b", 11],
      Up: ["b", 12], Down: ["b", 13], Left: ["b", 14], Right: ["b", 15],
      LSright: ["a", 0, 1, 0], LSup: ["a", 1, -1, 0], RSleft: ["a", 2, -1, 0], RSdown: ["a", 3, 1, 0] }),
    linux: merge(face, trig, sticks, {
      Back: ["b", 6], Start: ["b", 7], L3: ["b", 9], R3: ["b", 10],
      Up: ["a", 7, -1, 0], Down: ["a", 7, 1, 0], Left: ["a", 6, -1, 0], Right: ["a", 6, 1, 0] }),
    linuxwireless: merge(face, trig, sticks, {
      Back: ["b", 6], Start: ["b", 7], L3: ["b", 9], R3: ["b", 10],
      Left: ["b", 11], Right: ["b", 12], Up: ["b", 13], Down: ["b", 14] }),
    mac: merge(face, trig, sticks, {
      L3: ["b", 6], R3: ["b", 7], Start: ["b", 8], Back: ["b", 9],
      Up: ["b", 11], Down: ["b", 12], Left: ["b", 13], Right: ["b", 14] })
  };
  L.thirdparty = L.linux;
  L = L[kind];
  function press(name, on) {
    var c = L[name];
    if (c[0] === "b") setButton(c[1], on ? 1 : 0);
    else setAxis(c[1], on ? c[2] : c[3]);
  }
  var steps = ["A", "X", "Y", "LB", "RB", "Back", "L3", "Up", "Down", "Left", "Right", "LT", "RT",
               "LSright", "LSup", "RSleft", "RSdown", "R3", "B", "Start", "B"];

  function run() {
    console.log("[FakePad] " + kind + " layout: pressing " + steps.join(" "));
    var i = 0;
    (function next() {
      if (i >= steps.length) { setTimeout(function () { console.log("[FakePad] done"); }, 800); return; }
      var name = steps[i++];
      console.log("[FakePad] press " + name);
      press(name, true);
      setTimeout(function () { press(name, false); setTimeout(next, 350); }, 250);
    })();
  }
  var log = console.log.bind(console), started = false, connected = false;
  console.log = function () {
    log.apply(console, arguments);
    var msg = String(arguments[0]);
    if (!started && msg.indexOf("[PadTest] gamepad:") >= 0) { started = true; setTimeout(run, 1000); }
    if (!started && msg.indexOf("[PadTest] ready") >= 0 && !connected) {
      // a browser reveals a pad on its first button press, with a gamepadconnected event
      connected = true;
      press("A", true);
      var ev = new Event("gamepadconnected");
      Object.defineProperty(ev, "gamepad", { value: pad });
      window.dispatchEvent(ev);
      setTimeout(function () { press("A", false); }, 200);
    }
  };
})();
