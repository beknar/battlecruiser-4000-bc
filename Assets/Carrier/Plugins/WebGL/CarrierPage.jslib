// Calls from the game to the web page around it (Assets/WebGLTemplates/StarSystem).
mergeInto(LibraryManager.library, {
  // shows which graphics API the game started with (WebGPU, or the WebGL 2 fallback)
  CarrierPageGraphics: function (name) {
    var el = document.getElementById("api");
    var text = UTF8ToString(name);
    if (el) el.textContent = text;
    console.log("[Page] graphics: " + text + (el ? "" : " (no badge on this page)"));
  },
  // gamepad rumble (Unity's web player has none): the page's xbox360.js plays it through the
  // browser's vibration support (Chrome and Edge do for an Xbox 360 pad); 0, 0 stops it
  CarrierPadRumble: function (low, high, ms) {
    if (typeof window.carrierRumble === "function") window.carrierRumble(low, high, ms);
  },
  // Quit from the pause menu. A page can't close its own tab, and the engine can't be shut down
  // in a page that stays open: its own main loop and mouse and key listeners outlive it, and
  // call into freed memory ("memory access out of bounds", "null function"). So the game has
  // saved; this writes the save to the browser's storage, after the frame, and the page then
  // leaves for its "ended" page (window.carrierQuit). Without the page's handler, nothing happens.
  CarrierPageQuit: function () {
    console.log("[Page] quit");
    var left = false;
    function leave() {
      if (left) return;
      left = true;
      if (typeof window.carrierQuit === "function") window.carrierQuit();
    }
    setTimeout(function () {
      try {
        FS.syncfs(false, function (err) {
          if (err) console.warn("[Page] the save didn't reach the browser's storage: " + err);
          leave();
        });
      } catch (e) { leave(); }
      setTimeout(leave, 2000);   // never stuck on a sync that doesn't come back
    }, 0);
  },
});
