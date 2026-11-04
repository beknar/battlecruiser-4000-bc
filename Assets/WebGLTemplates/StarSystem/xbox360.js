// Xbox 360 controller support for the web build.
//
// Unity's web player only treats a controller as a gamepad when the browser reports it with
// the W3C "standard" mapping. Chrome and Edge on Windows do that for an Xbox 360 pad (XInput),
// but some browser / OS pairs report it raw, with buttons and axes in the driver's own order:
// Firefox on Linux (the xpad driver, which also runs most third-party XInput pads) and the macOS
// 360Controller driver. Unity would then see a generic joystick and the game would ignore it.
// This script, loaded before the game, presents those pads to the game in the standard layout.
// Standard pads pass through untouched.
(function () {
  "use strict";
  var raw = navigator.getGamepads ? navigator.getGamepads.bind(navigator) : null;
  if (!raw) return;

  var platform = (navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || navigator.userAgent || "";
  var onMac = /mac/i.test(platform), onLinux = /linux/i.test(platform) && !/android/i.test(navigator.userAgent || "");

  // Microsoft's vendor id with a 360 pad's product id (wired 028e, 028f; wireless receiver 0719),
  // with or without leading zeros (Firefox on macOS drops them), or the name
  function isXbox360(pad) {
    var id = (pad.id || "").toLowerCase();
    return id.indexOf("360") >= 0 && (id.indexOf("xbox") >= 0 || id.indexOf("x-box") >= 0) ||
           /\b0*45e-0*(28e|28f|719)\b/.test(id);
  }

  // standard indices: 0 A, 1 B, 2 X, 3 Y, 4 LB, 5 RB, 6 LT, 7 RT, 8 Back, 9 Start, 10 L3, 11 R3,
  // 12-15 D-pad up/down/left/right, 16 Guide
  var LINUX_WIRED = {      // xpad: 11 buttons, 8 axes (triggers and D-pad on axes)
    name: "linux xpad",
    buttons: { 0: 0, 1: 1, 2: 2, 3: 3, 4: 4, 5: 5, 6: 8, 7: 9, 8: 16, 9: 10, 10: 11 },
    sticks: [0, 1, 3, 4], triggers: [2, 5], dpadAxes: [6, 7]
  };
  var LINUX_DPAD_BUTTONS = {   // xpad with the D-pad as buttons (the wireless receiver): 15 buttons, 6 axes
    name: "linux xpad (D-pad buttons)",
    buttons: { 0: 0, 1: 1, 2: 2, 3: 3, 4: 4, 5: 5, 6: 8, 7: 9, 8: 16, 9: 10, 10: 11, 11: 14, 12: 15, 13: 12, 14: 13 },
    sticks: [0, 1, 3, 4], triggers: [2, 5], dpadAxes: null
  };
  var MAC = {              // 360Controller: 15 buttons, 6 axes (triggers on axes, D-pad on buttons)
    name: "macOS 360Controller",
    buttons: { 0: 0, 1: 1, 2: 2, 3: 3, 4: 4, 5: 5, 6: 10, 7: 11, 8: 9, 9: 8, 10: 16, 11: 12, 12: 13, 13: 14, 14: 15 },
    sticks: [0, 1, 3, 4], triggers: [2, 5], dpadAxes: null
  };
  function layoutFor(pad) {
    var nb = pad.buttons.length, na = pad.axes.length;
    if (onLinux) {
      // xpad's own shape: any pad it drives (Xbox 360 or a third-party XInput pad) looks like this
      if (nb === 11 && na === 8) return LINUX_WIRED;
      if (nb === 15 && na === 6 && isXbox360(pad)) return LINUX_DPAD_BUTTONS;
      return null;
    }
    if (onMac && isXbox360(pad) && nb >= 15 && na >= 6) return MAC;
    return null;
  }

  // A trigger on an axis rests at -1 and goes to +1 when pulled. Some browsers report 0 until it
  // is first moved, which would read as half pulled: until the rest position has been seen, 0
  // counts as released. (Forgotten when the pad disconnects.)
  var restSeen = {};
  function trigger(padIndex, axis, v) {
    var key = padIndex + ":" + axis;
    if (v < -0.5) restSeen[key] = true;
    if (!restSeen[key] && v === 0) return 0;
    return Math.max(0, Math.min(1, (v + 1) / 2));
  }

  // one standard-layout copy per raw pad, updated in place on every poll (no garbage each frame)
  var cache = {};
  function forget(index) {
    delete cache[index];
    for (var k in restSeen) if (k.indexOf(index + ":") === 0) delete restSeen[k];
  }
  function setButton(out, i, value) {
    var b = out.buttons[i];
    b.value = value; b.pressed = value > 0.5; b.touched = value > 0.05;
  }
  function standardise(pad) {
    if (!pad || pad.mapping === "standard") return pad;
    var lay = layoutFor(pad);
    if (!lay) return pad;
    var out = cache[pad.index];
    if (!out || out.rawId !== pad.id) {
      out = cache[pad.index] = { rawId: pad.id, id: pad.id + " (standard layout)", mapping: "standard", axes: [0, 0, 0, 0], buttons: [] };
      for (var n = 0; n < 17; n++) out.buttons.push({ pressed: false, touched: false, value: 0 });
      console.log("[Pad] " + pad.id + ": raw " + lay.name + " layout, shown to the game as a standard gamepad");
    }
    out.index = pad.index;
    out.connected = pad.connected;
    out.timestamp = pad.timestamp;
    out.vibrationActuator = pad.vibrationActuator || null;
    for (var i = 0; i < 17; i++) setButton(out, i, 0);
    for (var from in lay.buttons) {
      var src = pad.buttons[+from];
      if (src) setButton(out, lay.buttons[from], typeof src === "object" ? (src.value || (src.pressed ? 1 : 0)) : src);
    }
    setButton(out, 6, trigger(pad.index, lay.triggers[0], pad.axes[lay.triggers[0]] || 0));
    setButton(out, 7, trigger(pad.index, lay.triggers[1], pad.axes[lay.triggers[1]] || 0));
    if (lay.dpadAxes) {
      var dx = pad.axes[lay.dpadAxes[0]] || 0, dy = pad.axes[lay.dpadAxes[1]] || 0;
      setButton(out, 12, dy < -0.5 ? 1 : 0); setButton(out, 13, dy > 0.5 ? 1 : 0);
      setButton(out, 14, dx < -0.5 ? 1 : 0); setButton(out, 15, dx > 0.5 ? 1 : 0);
    }
    var s = lay.sticks;
    for (var a = 0; a < 4; a++) out.axes[a] = pad.axes[s[a]] || 0;
    return out;
  }

  var list = [];
  navigator.getGamepads = function () {
    var pads = raw();
    list.length = pads.length;
    for (var i = 0; i < pads.length; i++) list[i] = standardise(pads[i]);
    return list;
  };

  // connection events carry the raw pad too: pass a standardised one on instead
  function relay(type) {
    window.addEventListener(type, function (e) {
      if (e.carrierRelayed || !e.gamepad) return;
      if (typeof e.gamepad.index === "number") forget(e.gamepad.index);   // a new pad, or one gone: start afresh
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

  // Rumble, which Unity's web player doesn't do itself (the game calls this through a jslib):
  // low = the heavy motor, high = the light one (0..1), for ms; nothing at all stops it. A new
  // effect replaces the one playing, so the game passes the full time it still wants.
  window.carrierRumble = function (low, high, ms) {
    var pads = raw();
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
