// A stand-in mouse for the web quit check (quittest.sh): moves, clicks and wheels over the game's
// canvas every 100 ms for as long as the page is up, as a player's mouse would, including over
// the page that's left when the game quits. ?fakequit=N quits from the page N seconds after the
// game is up (window.carrierQuit), for a build too old to have ?quittest.
(function () {
  var n = 0;
  var q = new URLSearchParams(location.search);
  function at(c) {
    var r = c.getBoundingClientRect();
    return { clientX: r.left + Math.random() * r.width, clientY: r.top + Math.random() * r.height, bubbles: true, cancelable: true, view: window };
  }
  setInterval(function () {
    var c = document.getElementById("unity-canvas");
    if (!c) return;
    var p = at(c);
    c.dispatchEvent(new MouseEvent("mousemove", p));
    if (n % 5 === 0) {
      c.dispatchEvent(new MouseEvent("mousedown", p));
      c.dispatchEvent(new MouseEvent("mouseup", p));
    }
    if (n % 7 === 0) c.dispatchEvent(new WheelEvent("wheel", Object.assign({ deltaY: n % 14 ? 100 : -100 }, p)));
    n++;
  }, 100);
  setInterval(function () { console.log("[FakeMouse] events " + n); }, 5000);
  var after = parseFloat(q.get("fakequit"));
  if (after > 0) {
    var up = setInterval(function () {
      if (typeof window.carrierQuit !== "function") return;
      clearInterval(up);
      console.log("[FakeMouse] the game is up: quitting from the page in " + after + " s");
      setTimeout(function () {
        console.log("[FakeMouse] quitting from the page");
        window.carrierQuit();
      }, after * 1000);
    }, 250);
  }
})();
