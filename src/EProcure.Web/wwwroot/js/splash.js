// Splash auto-continue with a visible link fallback
document.addEventListener('DOMContentLoaded', function () {
  var splash = document.querySelector('[data-ep-splash]');
  if (!splash) return;
  var timeout = setTimeout(function () {
    var login = document.querySelector('a[data-ep-continue]');
    if (login) login.click();
  }, 2500);
  var skip = document.querySelector('a[data-ep-skip]');
  if (skip) skip.addEventListener('click', function () { clearTimeout(timeout); });
});
