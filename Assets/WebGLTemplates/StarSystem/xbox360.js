// Xbox 360 controller support for the web build.
//
// Unity's web player only treats a controller as a gamepad when the browser reports it with
// the W3C "standard" mapping. Chrome and Edge on Windows do that for an Xbox 360 pad (XInput),
// but some browser / OS pairs report it raw, with buttons and axes in the driver's own order:
// Firefox on Linux (the xpad driver) and the macOS 360Controller driver. Unity would then see
// a generic joystick and the game would ignore it. This script, loaded before the game,
// presents those pads to the game in the standard layout. Standard pads pass through untouched.
(function () {
  "use strict";
  var raw = navigator.getGamepads ? navigator.getGamepads.bind(navigator) : null;
  if (!raw) return;

  function isXbox360(pad) {
    var id = (pad.id || "").toLowerCase();
    return id.indexOf("360") >= 0 && (id.indexOf("xbox") >= 0 || id.indexOf("x-box") >= 0) ||
           /045e.{0,3}0?(28e|28f|719)\b/.test(id);    // Microsoft vendor id with a 360 pad's product id
  }

  // the raw layouts we know, picked by how many buttons and axes the pad reports
  var LINUX = {   // xpad: 11 buttons, 8 axes (triggers and D-pad on axes)
    name: "linux",
    buttons: { 0: 0, 1: 1, 2: 2, 3: 3, 4: 4, 5: 5, 6: 8, 7: 9, 8: 16, 9: 10, 10: 11 },
    sticks: [0, 1, 3, 4], triggers: [2, 5], dpadAxes: [6, 7]
  };
  var MAC = {     // 360Controller: 15 buttons, 6 axes (triggers on axes, D-pad on buttons)
    name: "mac",
    buttons: { 0: 0, 1: 1, 2: 2, 3: 3, 4: 4, 5: 5, 6: 10, 7: 11, 8: 9, 9: 8, 10: 16, 11: 12, 12: 13, 13: 14, 14: 15 },
    sticks: [0, 1, 3, 4], triggers: [2, 5], dpadAxes: null
  };
  function layoutFor(pad) {
    if (pad.buttons.length === 11 && pad.axes.length >= 8) return LINUX;
    if (pad.buttons.length >= 15 && pad.axes.length >= 6) return MAC;
    return null;
  }

  // A trigger on an axis rests at -1 and goes to +1 when pulled. Some browsers report 0 until it
  // is first moved, which would read as half pulled: until the rest position has been seen, 0
  // counts as released.
  var restSeen = {};
  function trigger(padIndex, axis, v) {
    var key = padIndex + ":" + axis;
    if (v < -0.5) restSeen[key] = true;
    if (!restSeen[key] && v === 0) return 0;
    return Math.max(0, Math.min(1, (v + 1) / 2));
  }

  function button(value) { return { pressed: value > 0.5, touched: value > 0.05, value: value }; }

  var cache = {};
  function standardise(pad) {
    if (!pad || pad.mapping === "standard" || !isXbox360(pad)) return pad;
    var lay = layoutFor(pad);
    if (!lay) return pad;
    var b = new Array(17);
    for (var i = 0; i < 17; i++) b[i] = 0;
    for (var from in lay.buttons) {
      var src = pad.buttons[+from];
      if (src) b[lay.buttons[from]] = typeof src === "object" ? src.value || (src.pressed ? 1 : 0) : src;
    }
    b[6] = trigger(pad.index, lay.triggers[0], pad.axes[lay.triggers[0]] || 0);
    b[7] = trigger(pad.index, lay.triggers[1], pad.axes[lay.triggers[1]] || 0);
    if (lay.dpadAxes) {
      var dx = pad.axes[lay.dpadAxes[0]] || 0, dy = pad.axes[lay.dpadAxes[1]] || 0;
      b[12] = dy < -0.5 ? 1 : 0; b[13] = dy > 0.5 ? 1 : 0; b[14] = dx < -0.5 ? 1 : 0; b[15] = dx > 0.5 ? 1 : 0;
    }
    var s = lay.sticks;
    var out = cache[pad.index] || (cache[pad.index] = { vibrationActuator: pad.vibrationActuator || null });
    out.id = pad.id + " (standard layout)";
    out.index = pad.index;
    out.connected = pad.connected;
    out.mapping = "standard";
    out.timestamp = pad.timestamp;
    out.axes = [pad.axes[s[0]] || 0, pad.axes[s[1]] || 0, pad.axes[s[2]] || 0, pad.axes[s[3]] || 0];
    out.buttons = b.map(button);
    if (!out.logged) { out.logged = true; console.log("[Pad] " + pad.id + ": raw " + lay.name + " layout, shown to the game as a standard gamepad"); }
    return out;
  }

  navigator.getGamepads = function () {
    var pads = raw();
    var list = [];
    for (var i = 0; i < pads.length; i++) list.push(standardise(pads[i]));
    return list;
  };

  // connection events carry the raw pad too: pass a standardised one on instead
  function relay(type) {
    window.addEventListener(type, function (e) {
      if (e.carrierRelayed || !e.gamepad) return;
      var std = standardise(e.gamepad);
      if (std === e.gamepad) return;
      e.stopImmediatePropagation();
      var ev = new Event(type);
      Object.defineProperty(ev, "gamepad", { value: std });
      Object.defineProperty(ev, "carrierRelayed", { value: true });
      window.dispatchEvent(ev);
    }, true);
  }
  relay("gamepadconnected");
  relay("gamepaddisconnected");

  // rumble, which Unity's web player doesn't do itself (the game calls this through a jslib)
  // (low = the heavy motor, high = the light one, 0..1; nothing at all stops it)
  window.carrierRumble = function (low, high, ms) {
    var pads = navigator.getGamepads();
    for (var i = 0; i < pads.length; i++) {
      var a = pads[i] && pads[i].vibrationActuator;
      if (!a) continue;
      try {
        if (low <= 0 && high <= 0 || ms <= 0) { if (a.reset) a.reset().catch(function () {}); continue; }
        if (a.playEffect)
          a.playEffect("dual-rumble", { duration: ms, strongMagnitude: Math.min(1, low), weakMagnitude: Math.min(1, high) })
           .catch(function () {});
      } catch (err) { }
    }
  };
})();
