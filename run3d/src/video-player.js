// Native video exposes the same small interface used by the YouTube adapter.
export const PLAYER_STATE = { ENDED: 0, PLAYING: 1, PAUSED: 2, BUFFERING: 3, CUED: 5 };

export class LocalVideoPlayer {
  constructor(container, { src, startTime = 0, events, title = '다운로드 영상' }) {
    this.continuousRate = true;
    this.title = title;
    this.destroyed = false;
    const video = this.video = document.createElement('video');
    video.id = 'player';
    video.controls = false;
    video.playsInline = true;
    video.muted = true;
    video.preload = 'auto';
    video.addEventListener('loadedmetadata', () => {
      if (this.destroyed) return;
      if (startTime < video.duration) video.currentTime = startTime;
      events.onReady({ target: this });
    }, { once: true });
    video.addEventListener('error', () => {
      if (!this.destroyed) events.onError({ data: `local-${video.error?.code || 0}` });
    });
    this.events = events;
    container.replaceChildren(video);
    video.src = src;
  }
  getDuration() { return this.video.duration; }
  getCurrentTime() { return this.video.currentTime; }
  getPlayerState() {
    if (this.video.ended) return PLAYER_STATE.ENDED;
    if (this.video.paused) return PLAYER_STATE.PAUSED;
    return this.video.readyState < 3 || this.video.seeking ? PLAYER_STATE.BUFFERING : PLAYER_STATE.PLAYING;
  }
  getPlaybackRate() { return this.video.playbackRate; }
  getAvailablePlaybackRates() { return []; }
  setPlaybackRate(rate) { this.video.playbackRate = rate; }
  seekTo(time) { this.video.currentTime = Math.min(this.video.duration, Math.max(0, time)); }
  playVideo() {
    this.video.play()?.catch((error) => {
      if (this.destroyed || error.name === 'AbortError') return;
      if (error.name === 'NotAllowedError') this.events.onAutoplayBlocked();
      else this.events.onError({ data: 'local-play' });
    });
  }
  pauseVideo() { this.video.pause(); }
  mute() { this.video.muted = true; }
  unMute() { this.video.muted = false; }
  isMuted() { return this.video.muted; }
  getVideoData() { return { title: this.title }; }
  destroy() {
    this.destroyed = true;
    this.video.pause();
    this.video.removeAttribute('src');
    this.video.load();
    this.video.remove();
  }
}
