const REPOSITORY = 'nekobyran/kacha';
const RELEASES_URL = `https://api.github.com/repos/${REPOSITORY}/releases/latest`;
const RELEASE_CACHE_SECONDS = 300;
const GITHUB_TIMEOUT_MS = 8_000;
const CSP = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'; font-src 'self'; media-src 'none'; worker-src 'none'; upgrade-insecure-requests";

function applySecurityHeaders(headers, contentType = '') {
  headers.set('Content-Security-Policy', CSP);
  headers.set('Referrer-Policy', 'strict-origin-when-cross-origin');
  headers.set('X-Content-Type-Options', 'nosniff');
  headers.set('X-Frame-Options', 'DENY');
  headers.set('Permissions-Policy', 'accelerometer=(), autoplay=(), camera=(), display-capture=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()');
  headers.set('Strict-Transport-Security', 'max-age=31536000; includeSubDomains');
  headers.set('Cross-Origin-Opener-Policy', 'same-origin');
  headers.set('Cross-Origin-Resource-Policy', 'same-origin');
  if (contentType.includes('text/html')) {
    headers.set('Cache-Control', 'public, max-age=0, must-revalidate, no-transform');
  }
}

function jsonResponse(payload, status = 200, method = 'GET') {
  const headers = new Headers({
    'Content-Type': 'application/json; charset=utf-8',
    'Cache-Control': status === 200
      ? `public, max-age=${RELEASE_CACHE_SECONDS}, s-maxage=${RELEASE_CACHE_SECONDS}, stale-while-revalidate=600, no-transform`
      : 'no-store',
  });
  applySecurityHeaders(headers, 'application/json');
  return new Response(method === 'HEAD' ? null : JSON.stringify(payload), { status, headers });
}

function withoutBody(response) {
  return new Response(null, {
    status: response.status,
    statusText: response.statusText,
    headers: response.headers,
  });
}

async function githubFetch(url, headers = {}) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), GITHUB_TIMEOUT_MS);
  try {
    return await fetch(url, {
      headers: {
        Accept: 'application/vnd.github+json',
        'User-Agent': 'nkbr-kacha-release-site',
        'X-GitHub-Api-Version': '2022-11-28',
        ...headers,
      },
      signal: controller.signal,
    });
  } finally {
    clearTimeout(timer);
  }
}

function fallbackPayload(code) {
  return {
    ok: false,
    code,
    repository: REPOSITORY,
    fallbackUrl: `https://github.com/${REPOSITORY}/releases`,
  };
}

async function buildLatestReleaseResponse() {
  const githubResponse = await githubFetch(RELEASES_URL);
  if (!githubResponse.ok) {
    return jsonResponse(fallbackPayload('GITHUB_RELEASE_UNAVAILABLE'), 502);
  }

  const release = await githubResponse.json();
  if (!release || release.draft || release.prerelease) {
    return jsonResponse(fallbackPayload('NO_STABLE_RELEASE'), 404);
  }

  const assets = Array.isArray(release.assets) ? release.assets.slice(0, 50) : [];
  const setupAsset = assets.find((item) => /^ScreenshotCat-.*-win-x64-setup\.exe$/i.test(item?.name || ''));
  const portableAsset = assets.find((item) => /^ScreenshotCat-.*-win-x64\.zip$/i.test(item?.name || ''));
  const asset = setupAsset || portableAsset;

  if (!asset?.browser_download_url) {
    return jsonResponse(fallbackPayload('WINDOWS_X64_ASSET_NOT_FOUND'), 404);
  }

  return jsonResponse({
    ok: true,
    repository: REPOSITORY,
    repositoryUrl: `https://github.com/${REPOSITORY}`,
    license: {
      spdx: 'MIT',
      url: `https://github.com/${REPOSITORY}/blob/main/LICENSE`,
    },
    tag: String(release.tag_name || '').slice(0, 96),
    publishedAt: String(release.published_at || '').slice(0, 64),
    releaseUrl: String(release.html_url || `https://github.com/${REPOSITORY}/releases`).slice(0, 512),
    asset: {
      name: String(asset.name || '').slice(0, 255),
      size: Number.isSafeInteger(asset.size) ? asset.size : 0,
      downloadUrl: String(asset.browser_download_url).slice(0, 1024),
      contentType: String(asset.content_type || 'application/octet-stream').slice(0, 128),
    },
  });
}

async function discoverLatestRelease(request, context) {
  const origin = new URL(request.url).origin;
  const cacheKey = new Request(`${origin}/api/release`, { method: 'GET' });
  const cache = caches.default;
  const cached = await cache.match(cacheKey);
  if (cached) return request.method === 'HEAD' ? withoutBody(cached) : cached;

  const response = await buildLatestReleaseResponse();
  if (response.ok) context.waitUntil(cache.put(cacheKey, response.clone()));
  return request.method === 'HEAD' ? withoutBody(response) : response;
}

export default {
  async fetch(request, env, context) {
    const url = new URL(request.url);
    if (!['GET', 'HEAD'].includes(request.method)) {
      const headers = new Headers({ Allow: 'GET, HEAD' });
      applySecurityHeaders(headers, 'text/plain');
      return new Response('Method Not Allowed', { status: 405, headers });
    }

    if (url.pathname === '/api/release') {
      try {
        return await discoverLatestRelease(request, context);
      } catch {
        return jsonResponse(fallbackPayload('RELEASE_DISCOVERY_FAILED'), 502, request.method);
      }
    }

    const response = await env.ASSETS.fetch(request);
    const headers = new Headers(response.headers);
    const contentType = headers.get('Content-Type') || '';
    applySecurityHeaders(headers, contentType);
    return new Response(request.method === 'HEAD' ? null : response.body, {
      status: response.status,
      statusText: response.statusText,
      headers,
    });
  },
};
