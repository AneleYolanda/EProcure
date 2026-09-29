// eProcure: small progressive enhancements. Every page also works with JavaScript switched off.
(function () {
  "use strict";

  // 1. Splash screens: continue automatically after a short delay (the design's auto-advance).
  //    <div data-ep-autocontinue="/Account/Login" data-ep-delay="2500">
  var splash = document.querySelector("[data-ep-autocontinue]");
  if (splash) {
    var target = splash.getAttribute("data-ep-autocontinue");
    var delay = parseInt(splash.getAttribute("data-ep-delay") || "2500", 10);
    if (target && target.charAt(0) === "/") { // only ever navigate within this site
      window.setTimeout(function () { window.location.assign(target); }, delay);
    }
  }

  // 2. OTP boxes: one digit per box, move to the next box as you type, paste a whole code at once.
  //    The boxes are normal form fields named "Digits", so the form also posts without JavaScript.
  var otp = document.querySelector("[data-ep-otp]");
  if (otp) {
    var boxes = Array.prototype.slice.call(otp.querySelectorAll("input"));
    boxes.forEach(function (box, i) {
      box.addEventListener("input", function () {
        box.value = box.value.replace(/\D/g, "").slice(-1);
        if (box.value && i < boxes.length - 1) boxes[i + 1].focus();
      });
      box.addEventListener("keydown", function (e) {
        if (e.key === "Backspace" && !box.value && i > 0) boxes[i - 1].focus();
      });
      box.addEventListener("paste", function (e) {
        var text = (e.clipboardData || window.clipboardData).getData("text").replace(/\D/g, "");
        if (!text) return;
        e.preventDefault();
        boxes.forEach(function (b, j) { b.value = text.charAt(j) || ""; });
        boxes[Math.min(text.length, boxes.length) - 1].focus();
      });
    });
    if (boxes.length && !boxes[0].value) boxes[0].focus();
  }

  // 3. Admin console: open/close the sidebar drawer on small screens.
  var consoleEl = document.querySelector(".ep-console");
  document.querySelectorAll("[data-ep-menu]").forEach(function (btn) {
    btn.addEventListener("click", function () {
      if (!consoleEl) return;
      var open = consoleEl.classList.toggle("is-menu-open");
      document.querySelectorAll("[data-ep-menu]").forEach(function (b) { b.setAttribute("aria-expanded", String(open)); });
    });
  });

  // 5. Tender form: running total of the functionality weights (they must add up to 100; the server checks it too).
  var weights = document.querySelector("[data-ep-weights]");
  if (weights) {
    var totalEl = weights.querySelector("[data-ep-weight-total]");
    var update = function () {
      var total = 0;
      weights.querySelectorAll("[data-ep-weight]").forEach(function (input) { total += parseInt(input.value || "0", 10) || 0; });
      if (totalEl) totalEl.textContent = "Now: " + total + (total === 100 ? " (correct)" : "");
    };
    weights.addEventListener("input", update);
    update();
  }

  // 4. Admin sign-in (Development only): clicking a demo account card fills in its email address.
  document.querySelectorAll("[data-ep-fill-email]").forEach(function (card) {
    card.addEventListener("click", function () {
      var input = document.querySelector("[data-ep-email-input]");
      if (input) input.value = card.getAttribute("data-ep-fill-email");
      document.querySelectorAll("[data-ep-fill-email]").forEach(function (c) {
        c.classList.toggle("ep-choice--selected", c === card);
        c.setAttribute("aria-pressed", String(c === card));
      });
      var pwd = document.querySelector("[data-ep-password-input]");
      if (pwd) pwd.focus();
    });
  });
})();
