const RELEASES_FALLBACK = 'https://github.com/nekobyran/kacha/releases';

const year = document.querySelector('[data-year]');
const checksum = document.querySelector('[data-checksum]');
const checksumCommand = document.querySelector('[data-checksum-command]');
const copyButton = document.querySelector('[data-copy-checksum]');
const toast = document.querySelector('[data-toast]');
const releaseState = document.querySelector('[data-release-version]');
const releaseTitle = document.querySelector('[data-release-title]');
const releaseDate = document.querySelector('[data-release-date]');
const releaseFile = document.querySelector('[data-release-file]');
const releaseSize = document.querySelector('[data-release-size]');
const releaseLink = document.querySelector('[data-release-link]');
const downloadLinks = [...document.querySelectorAll('[data-download]')];

if (year) year.textContent = String(new Date().getFullYear());

document.querySelectorAll('img').forEach((image) => {
  const source = image.getAttribute('src');
  if (!source || !source.trim()) image.remove();
});

const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
const connection = navigator.connection || navigator.mozConnection || navigator.webkitConnection;
const lowMotion = reducedMotion.matches || connection?.saveData === true;
const revealTargets = [...document.querySelectorAll('main > section, .hero-copy, .capture-demo')];
const revealAll = () => revealTargets.forEach((target) => target.classList.add('is-visible'));

if ('IntersectionObserver' in window && !lowMotion) {
  document.documentElement.classList.add('reveal-enhanced');
  const observer = new IntersectionObserver((entries) => {
    entries.forEach((entry) => {
      if (!entry.isIntersecting && entry.boundingClientRect.top >= innerHeight) return;
      entry.target.classList.add('is-visible');
      observer.unobserve(entry.target);
    });
  }, { rootMargin: '0px 0px -7% 0px', threshold: 0.06 });
  revealTargets.forEach((target) => {
    target.classList.add('reveal-target');
    observer.observe(target);
  });
  window.setTimeout(() => {
    revealAll();
    observer.disconnect();
  }, 900);
} else {
  document.documentElement.classList.add('is-motion-off');
  revealAll();
}

let toastTimer;

function showToast(message) {
  if (!toast) return;
  window.clearTimeout(toastTimer);
  toast.textContent = message;
  toast.classList.add('is-visible');
  toastTimer = window.setTimeout(() => toast.classList.remove('is-visible'), 2200);
}

function selectChecksum() {
  if (!checksum) return;
  const selection = window.getSelection();
  const range = document.createRange();
  range.selectNodeContents(checksum);
  selection?.removeAllRanges();
  selection?.addRange(range);
  checksum.focus();
}

function formatBytes(bytes) {
  if (!Number.isFinite(bytes) || bytes <= 0) return '未知';
  const units = ['B', 'KiB', 'MiB', 'GiB'];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  return `${value >= 100 ? value.toFixed(0) : value.toFixed(1)} ${units[unit]}`;
}

function formatDate(value) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '未知';
  return new Intl.DateTimeFormat('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(date).replaceAll('/', '.');
}

function setReleaseState(text, state = 'ready') {
  if (!releaseState) return;
  const dot = releaseState.querySelector('.status-dot');
  releaseState.replaceChildren();
  if (dot) releaseState.append(dot);
  releaseState.append(document.createTextNode(` ${text}`));
  releaseState.dataset.state = state;
}

function setReleaseTitle(primary, emphasis) {
  if (!releaseTitle) return;
  const lineBreak = document.createElement('br');
  const highlighted = document.createElement('em');
  highlighted.textContent = emphasis;
  releaseTitle.replaceChildren(document.createTextNode(primary), lineBreak, highlighted);
}

function applyRelease(release) {
  const tag = release.tag || 'latest';
  const version = tag.replace(/^v/i, '');
  const asset = release.asset;

  setReleaseState(`${tag} · 最新正式版`);
  setReleaseTitle(tag, '现在可用。');
  if (releaseDate) {
    releaseDate.textContent = formatDate(release.publishedAt);
    if (release.publishedAt) releaseDate.dateTime = release.publishedAt;
  }
  if (releaseLink) releaseLink.href = release.releaseUrl || RELEASES_FALLBACK;

  if (!asset?.downloadUrl) {
    throw new Error('No matching Windows x64 release asset');
  }

  if (releaseFile) releaseFile.textContent = asset.name;
  if (releaseSize) releaseSize.textContent = formatBytes(asset.size);
  if (checksum) checksum.textContent = release.sha256 || '请下载同版本 .sha256 文件核对';
  if (checksumCommand) checksumCommand.textContent = `Get-FileHash .\\${asset.name} -Algorithm SHA256`;

  downloadLinks.forEach((link, index) => {
    link.href = asset.downloadUrl;
    const strong = link.querySelector('strong');
    const small = link.querySelector('small');
    if (strong) strong.textContent = index === 0 ? `下载 ${tag}` : '下载 Windows x64';
    if (small) small.textContent = `${asset.name.endsWith('.exe') ? 'SETUP' : 'ZIP'} · ${formatBytes(asset.size)}`;
  });

  document.documentElement.dataset.release = version;
}

function applyReleaseFallback() {
  setReleaseState('无法读取 Release · 打开 GitHub 查看', 'fallback');
  setReleaseTitle('GitHub Releases', '查看最新版本。');
  if (releaseDate) releaseDate.textContent = '实时状态不可用';
  if (releaseFile) releaseFile.textContent = '请在 Releases 页面选择 Windows x64 Setup';
  if (releaseSize) releaseSize.textContent = '以 GitHub 显示为准';
  if (checksum) checksum.textContent = '请使用 Release 附带的 .sha256 文件';
  downloadLinks.forEach((link) => {
    link.href = RELEASES_FALLBACK;
    const strong = link.querySelector('strong');
    const small = link.querySelector('small');
    if (strong) strong.textContent = '打开 Releases';
    if (small) small.textContent = '自动识别暂不可用';
  });
}

async function loadLatestRelease() {
  try {
    const response = await fetch('/api/release', {
      headers: { Accept: 'application/json' },
      cache: 'no-store',
    });
    if (!response.ok) throw new Error(`Release API ${response.status}`);
    applyRelease(await response.json());
  } catch (error) {
    console.warn('Kacha release discovery failed', error);
    applyReleaseFallback();
  }
}

copyButton?.addEventListener('click', async () => {
  const value = checksum?.textContent?.trim();
  if (!value || !/^[a-f0-9]{64}$/i.test(value)) {
    showToast('当前没有可复制的 SHA-256');
    return;
  }

  try {
    await navigator.clipboard.writeText(value);
    copyButton.classList.add('is-copied');
    const label = copyButton.querySelector('span');
    if (label) label.textContent = '已复制';
    showToast('SHA-256 已复制');
    window.setTimeout(() => {
      copyButton.classList.remove('is-copied');
      if (label) label.textContent = '复制';
    }, 1800);
  } catch {
    selectChecksum();
    showToast('校验值已选中，请手动复制');
  }
});

downloadLinks.forEach((link) => {
  link.addEventListener('click', () => showToast('正在前往 GitHub Releases…'));
});

document.addEventListener('visibilitychange', () => {
  document.documentElement.classList.toggle('is-page-hidden', document.hidden);
});

void loadLatestRelease();
