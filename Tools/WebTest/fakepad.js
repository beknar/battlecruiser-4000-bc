// TEST ONLY (not shipped; used by Tools/WebTest/padtest.sh): a stand-in Xbox 360 controller for
// headless Chrome, which has no real one. ?fakepad=standard reports it as Chrome on Windows does
// (W3C standard mapping); ?fakepad=linux reports it raw, as Firefox on Linux does with the xpad
// driver. Once the game logs "[PadTest] ready", it connects, then presses each control in turn.
(function () {
  "use strict";
  var kind = (location.search.match(/fakepad=(\w+)/) || [])[1] || "standard";
  var std = kind === "standard";
  var nb = std ? 17 : 11, na = std ? 4 : 8;
  var buttons = [], axes = [];
  for (var i = 0; i < nb; i++) buttons.push({ pressed: false, touched: false, value: 0 });
  for (var j = 0; j < na; j++) axes.push(0);   // xpad triggers read 0 until first moved
  var pad = {
    id: std ? "Xbox 360 Controller (XInput STANDARD GAMEPAD)" : "045e-028e-Microsoft X-Box 360 pad",
    index: 0, connected: true, mapping: std ? "standard" : "", timestamp: performance.now(),
    buttons: buttons, axes: axes,
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

  // what each named control is on this layout: [kind, index, pressed value, released value]
  var L = std ? {
    A: ["b", 0], B: ["b", 1], X: ["b", 2], Y: ["b", 3], LB: ["b", 4], RB: ["b", 5], LT: ["b", 6], RT: ["b", 7],
    Back: ["b", 8], Start: ["b", 9], L3: ["b", 10], R3: ["b", 11], Up: ["b", 12], Down: ["b", 13], Left: ["b", 14], Right: ["b", 15],
    LSright: ["a", 0, 1, 0], LSup: ["a", 1, -1, 0], RSleft: ["a", 2, -1, 0], RSdown: ["a", 3, 1, 0]
  } : {
    A: ["b", 0], B: ["b", 1], X: ["b", 2], Y: ["b", 3], LB: ["b", 4], RB: ["b", 5], LT: ["a", 2, 1, -1], RT: ["a", 5, 1, -1],
    Back: ["b", 6], Start: ["b", 7], L3: ["b", 9], R3: ["b", 10], Up: ["a", 7, -1, 0], Down: ["a", 7, 1, 0], Left: ["a", 6, -1, 0], Right: ["a", 6, 1, 0],
    LSright: ["a", 0, 1, 0], LSup: ["a", 1, -1, 0], RSleft: ["a", 3, -1, 0], RSdown: ["a", 4, 1, 0]
  };
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
