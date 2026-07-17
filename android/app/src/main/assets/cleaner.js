(function () {
  var VERSION = 9;
  if (window.__cyclingTodayCleanPlayer && window.__cyclingTodayCleanPlayer.version === VERSION) {
    try { window.__cyclingTodayCleanPlayer.apply(); } catch (ignore) {}
    return;
  }

  var state = {
    found: false,
    candidates: 0,
    reason: 'starting',
    src: '',
    rect: { left: 0, top: 0, width: 0, height: 0 }
  };

  var blockedHostPattern = /(2mdn|adform|adnxs|adskeeper|adsterra|advertising|amazon-adsystem|analytics\.google|casalemedia|contextweb|criteo|doubleclick|exoclick|googlesyndication|googletagmanager|googletagservices|google-analytics|googleadservices|imasdk|mgid|onclickads|openx|outbrain|popads|popcash|propellerads|pubmatic|quantserve|revcontent|rubiconproject|scorecardresearch|taboola|trafficjunky|yllix|adsrvr|adroll|adservice|adsafeprotected|bluekai|demdex|everesttech|indexww|lijit|media\.net|moatads|sharethrough|smartadserver|spotxchange|springserve|teads|themoneytizer|yieldmo|zedo|zeotap|onesignal|twitter|x\.com|facebook|instagram)/i;
  var playerHintPattern = /(ok\.ru|videoembed|player|stream|embed|live|video|jwplayer|dailymotion|twitch|cloudfront|fastly|hls|dash|m3u8|\/e\/)/i;
  var liveEmbedPattern = /(ok\.ru|videoembed|\/e\/[a-z0-9_-]+|embed\/[a-z0-9_-]+|player|stream|live|m3u8|hls)/i;
  var nonLiveVideoHostPattern = /(^|\.)((youtube(-nocookie)?\.com)|(youtu\.be)|(vimeo\.com))$/i;
  var negativeFramePattern = /(chat|comment|poll|prediction|twitter|x\.com|facebook|instagram|telegram|discord|disqus|newsletter|googlesyndication|doubleclick|googleads|aswift)/i;
  var adLabelPattern = /(^|[\s_-])(ad|ads|advert|advertisement|sponsor|sponsored|banner|popup|popunder|overlay|sticky|outbrain|taboola|mgid|revcontent|adunit|ad-slot|native-ad)([\s_-]|$)/i;
  var focusing = false;
function hostOf(url) {
    try { return new URL(url, location.href).hostname || ''; } catch (ignore) { return ''; }
  }

  function frameSrc(frame) {
    return frame.src || frame.getAttribute('src') || frame.getAttribute('data-src') || frame.getAttribute('data-lazy-src') || '';
  }

  function isCyclingToday() {
    return /(^|\.)cycling\.today$/i.test(location.hostname || '');
  }

  function isProtectedNode(node, player) {
    return !!(player && (node === player || node.contains(player) || player.contains(node)));
  }

  function hideNode(node) {
    if (!node || node === document.documentElement || node === document.body) {
      return;
    }

    try {
      if (node.tagName === 'IFRAME') {
        node.src = 'about:blank';
        node.remove();
        return;
      }
    } catch (ignore) {}

    try { node.style.setProperty('display', 'none', 'important'); } catch (ignore) {}
    try { node.style.setProperty('visibility', 'hidden', 'important'); } catch (ignore) {}
    try { node.style.setProperty('pointer-events', 'none', 'important'); } catch (ignore) {}
  }

  try {
    Object.defineProperty(window, 'open', {
      value: function () { return null; },
      configurable: true
    });
  } catch (ignore) {}

  document.addEventListener('click', function (event) {
    var node = event.target;
    while (node && node !== document && node.tagName !== 'A') {
      node = node.parentNode;
    }

    if (!node || node === document || !node.href) {
      return;
    }

    try {
      var host = hostOf(node.href);
      if (blockedHostPattern.test(host)) {
        event.preventDefault();
        event.stopImmediatePropagation();
      }
    } catch (ignore) {}
  }, true);

  if (!isCyclingToday()) {
    return;
  }

  function scoreFrame(frame) {
    var rect = frame.getBoundingClientRect();
    var width = Math.max(rect.width, 0);
    var height = Math.max(rect.height, 0);
    var area = width * height;
    var src = frameSrc(frame);
    var host = hostOf(src);
    var allow = frame.getAttribute('allow') || '';
    var label = [src, host, allow, frame.id || '', frame.className || '', frame.name || ''].join(' ').toLowerCase();

    if (!src || src === 'about:blank' || width < 120 || height < 70 || area < 12000) {
      return -1;
    }

    if (blockedHostPattern.test(host) || nonLiveVideoHostPattern.test(host) || negativeFramePattern.test(label)) {
      return -1;
    }

    var score = area;
    if (playerHintPattern.test(label)) { score += area * 3; }
    if (liveEmbedPattern.test(label)) { score += area * 4; }
    if (/autoplay|fullscreen|encrypted-media|picture-in-picture/i.test(allow)) { score += area * 1.5; }
    if (frame.hasAttribute('allowfullscreen')) { score += area; }
    if (width >= 300 && height >= 160 && width / Math.max(height, 1) >= 1.2) { score += area; }
    if (/ok\.ru|videoembed/i.test(label)) { score += area * 8; }
    if (/ad|ads|banner|sponsor|popup|tracking/i.test(label)) { score -= area * 2; }

    return score;
  }

  function findPlayer() {
    var frames = Array.prototype.slice.call(document.querySelectorAll('iframe'));
    var best = null;
    var bestScore = -1;
    var candidates = 0;

    for (var i = 0; i < frames.length; i++) {
      var score = scoreFrame(frames[i]);
      if (score < 0) {
        continue;
      }
      candidates++;
      if (score > bestScore) {
        best = frames[i];
        bestScore = score;
      }
    }

    state.candidates = candidates;
    if (!best) {
      state.found = false;
      state.reason = 'player-not-found';
      state.src = '';
      return null;
    }

    return best;
  }

  function hideKnownClutter(player) {
    var selectors = [
      'ins.adsbygoogle',
      '.adsbygoogle',
      '[id^="google_ads"]',
      '[id^="aswift_"]',
      'iframe[src*="doubleclick"]',
      'iframe[src*="googlesyndication"]',
      'iframe[src*="googleads"]',
      'iframe[src*="googletag"]',
      'iframe[src*="taboola"]',
      'iframe[src*="outbrain"]',
      'iframe[src*="mgid"]',
      '.td-menu-background',
      '.td-search-background'
    ];

    try {
      document.querySelectorAll(selectors.join(',')).forEach(function (node) {
        if (!isProtectedNode(node, player)) {
          hideNode(node);
        }
      });
    } catch (ignore) {}

    try {
      Array.prototype.slice.call(document.querySelectorAll('div,section,aside,ins,iframe,amp-ad')).forEach(function (node) {
        if (isProtectedNode(node, player)) {
          return;
        }

        var label = [node.id || '', node.className || '', node.getAttribute('aria-label') || '', node.getAttribute('data-ad') || ''].join(' ');
        var style = window.getComputedStyle ? window.getComputedStyle(node) : null;
        var fixed = style && (style.position === 'fixed' || style.position === 'sticky');
        var z = style ? parseInt(style.zIndex || '0', 10) : 0;
        if (adLabelPattern.test(label) || (fixed && z >= 10)) {
          hideNode(node);
        }
      });
    } catch (ignore) {}
  }

  function disableNonPlayerFrames(player) {
    Array.prototype.slice.call(document.querySelectorAll('iframe')).forEach(function (frame) {
      if (frame === player) {
        return;
      }
      hideNode(frame);
    });
  }

  function hideAroundPlayer(player) {
    var child = player;
    var parent = player.parentElement;

    while (parent && parent !== document.body) {
      parent.style.setProperty('visibility', 'visible', 'important');
      parent.style.setProperty('display', '', 'important');
      parent.style.setProperty('background', 'transparent', 'important');

      Array.prototype.slice.call(parent.children).forEach(function (sibling) {
        if (sibling !== child) {
          sibling.style.setProperty('visibility', 'hidden', 'important');
          sibling.style.setProperty('pointer-events', 'none', 'important');
        }
      });

      child = parent;
      parent = parent.parentElement;
    }

    if (document.body) {
      Array.prototype.slice.call(document.body.children).forEach(function (bodyChild) {
        if (bodyChild !== child) {
          bodyChild.style.setProperty('visibility', 'hidden', 'important');
          bodyChild.style.setProperty('pointer-events', 'none', 'important');
        }
      });
    }

    player.style.setProperty('visibility', 'visible', 'important');
    player.style.setProperty('display', 'block', 'important');
    player.style.setProperty('pointer-events', 'auto', 'important');
  }

  function rememberPlayer(player) {
    var rect = player.getBoundingClientRect();
    state.found = true;
    state.reason = 'ok';
    state.src = frameSrc(player);
    state.rect = {
      left: Math.max(0, rect.left),
      top: Math.max(0, rect.top),
      width: Math.max(1, rect.width),
      height: Math.max(1, rect.height)
    };
  }

  function applyCleanLayout() {
    if (focusing || !document.body) {
      return false;
    }

    focusing = true;
    try {
      hideKnownClutter(null);
      document.body.style.transform = 'none';

      var player = findPlayer();
      if (!player) {
        return false;
      }

      hideKnownClutter(player);
      disableNonPlayerFrames(player);
      hideAroundPlayer(player);

      document.documentElement.style.cssText =
        'width:100%!important;height:100%!important;margin:0!important;padding:0!important;overflow:hidden!important;background:#000!important;';
      document.body.style.cssText =
        'margin:0!important;padding:0!important;background:#000!important;transform-origin:0 0!important;overflow:visible!important;';

      player.setAttribute('allow', 'autoplay; encrypted-media; fullscreen; picture-in-picture');
      player.setAttribute('allowfullscreen', 'true');
      window.scrollTo(0, 0);

      var rect = player.getBoundingClientRect();
      var width = Math.max(rect.width, 1);
      var height = Math.max(rect.height, 1);
      var scale = Math.min(window.innerWidth / width, window.innerHeight / height);
      if (!isFinite(scale) || scale <= 0) {
        scale = 1;
      }

      scale = Math.max(0.5, Math.min(scale, 2.5));
      var x = (window.innerWidth - width * scale) / 2 - rect.left * scale;
      var y = (window.innerHeight - height * scale) / 2 - rect.top * scale;
      document.body.style.transform = 'translate(' + x + 'px,' + y + 'px) scale(' + scale + ')';

      rememberPlayer(player);
      return true;
    } finally {
      focusing = false;
    }
  }

  function clamp(value, min, max) {
    return Math.max(min, Math.min(max, value));
  }

  function getClickPoints() {
    applyCleanLayout();
    if (!state.found || !state.rect || state.rect.width <= 1 || state.rect.height <= 1) {
      return [];
    }

    var r = state.rect;
    return [
      [r.left + r.width * 0.22, r.top + r.height * 0.08],
      [r.left + r.width * 0.50, r.top + r.height * 0.50],
      [r.left + r.width * 0.08, r.top + r.height * 0.88],
      [r.left + r.width * 0.50, r.top + r.height * 0.92]
    ];
  }

  function getClickPointText() {
    var points = getClickPoints();
    var maxX = Math.max(10, window.innerWidth - 10);
    var maxY = Math.max(10, window.innerHeight - 10);
    var text = [];
    for (var i = 0; i < points.length; i++) {
      var px = Math.round(clamp(points[i][0], 10, maxX));
      var py = Math.round(clamp(points[i][1], 10, maxY));
      var encoded = px + ',' + py;
      if (text.indexOf(encoded) === -1) {
        text.push(encoded);
      }
    }
    return text.join('|');
  }

  function getNormalizedClickPointText() {
    var points = getClickPoints();
    var viewportWidth = Math.max(1, window.innerWidth);
    var viewportHeight = Math.max(1, window.innerHeight);
    var text = [];
    for (var i = 0; i < points.length; i++) {
      var ratioX = clamp(points[i][0] / viewportWidth, 0.01, 0.99).toFixed(5);
      var ratioY = clamp(points[i][1] / viewportHeight, 0.01, 0.99).toFixed(5);
      var encoded = ratioX + ',' + ratioY;
      if (text.indexOf(encoded) === -1) {
        text.push(encoded);
      }
    }
    return text.join('|');
  }

  function getNormalizedFullScreenPointText() {
    applyCleanLayout();
    if (!state.found || !state.rect || state.rect.width <= 1 || state.rect.height <= 1) {
      return '';
    }
    var r = state.rect;
    var viewportWidth = Math.max(1, window.innerWidth);
    var viewportHeight = Math.max(1, window.innerHeight);
    return [
      [r.left + r.width - 24, r.top + r.height - 22],
      [r.left + r.width - 48, r.top + r.height - 22],
      [r.left + r.width - 24, r.top + r.height - 46]
    ].map(function (point) {
      return clamp(point[0] / viewportWidth, 0.01, 0.99).toFixed(5) + ',' +
        clamp(point[1] / viewportHeight, 0.01, 0.99).toFixed(5);
    }).join('|');
  }
  function getStateText() {
    applyCleanLayout();
    var rect = state.rect || { left: 0, top: 0, width: 0, height: 0 };
    return 'playerFound=' + state.found +
      ' candidates=' + state.candidates +
      ' reason=' + state.reason +
      ' rect=' + Math.round(rect.left) + ',' + Math.round(rect.top) + ',' + Math.round(rect.width) + 'x' + Math.round(rect.height) +
      ' src=' + (state.src || '').slice(0, 180);
  }

  window.__cyclingTodayCleanPlayer = {
    version: VERSION,
    apply: applyCleanLayout,
    getClickPointText: getClickPointText,
    getNormalizedClickPointText: getNormalizedClickPointText,
    getNormalizedFullScreenPointText: getNormalizedFullScreenPointText,
    getStateText: getStateText
  };

  applyCleanLayout();
  try {
    new MutationObserver(applyCleanLayout).observe(document.documentElement, { childList: true, subtree: true, attributes: true });
  } catch (ignore) {}
  window.setInterval(applyCleanLayout, 750);
  window.addEventListener('resize', applyCleanLayout);
})();
