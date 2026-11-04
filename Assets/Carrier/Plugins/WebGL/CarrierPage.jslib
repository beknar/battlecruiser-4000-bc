// Calls from the game to the web page around it (Assets/WebGLTemplates/StarSystem).
mergeInto(LibraryManager.library, {
  // shows which graphics API the game started with (WebGPU, or the WebGL 2 fallback)
  CarrierPageGraphics: function (name) {
    var el = document.getElementById("api");
    var text = UTF8ToString(name);
    if (el) el.textContent = text;
    console.log("[Page] graphics: " + text + (el ? "" : " (no badge on this page)"));
  },
  // Quit from the pause menu: a page can't close its own tab, so the page shuts the game down
  // (and offers to start it again); without the page's handler, nothing happens
  CarrierPageQuit: function () {
    console.log("[Page] quit");
    if (typeof window.carrierQuit === "function") window.carrierQuit();
  },
});
