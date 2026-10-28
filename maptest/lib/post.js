// 후처리(화면 전체에 거는 효과):
//  - 계단 현상 제거(MSAA)
//  - 구석 그늘(GTAO, 화질 high에서만): 물체가 맞닿은 곳을 살짝 어둡게 해서 입체감을 준다
//  - 빛 번짐(블룸): 아주 밝은 곳만 은은하게
//  - 색감 보정(ACES 톤 매핑) + 약한 대비·채도 + 가장자리 어둡게(비네트)
import * as THREE from 'three';
import { EffectComposer } from 'three/addons/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/addons/postprocessing/RenderPass.js';
import { GTAOPass } from 'three/addons/postprocessing/GTAOPass.js';
import { UnrealBloomPass } from 'three/addons/postprocessing/UnrealBloomPass.js';
import { OutputPass } from 'three/addons/postprocessing/OutputPass.js';
import { ShaderPass } from 'three/addons/postprocessing/ShaderPass.js';

const GradeShader = {
  uniforms: { tDiffuse: { value: null } },
  vertexShader: `
    varying vec2 vUv;
    void main() { vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }`,
  fragmentShader: `
    uniform sampler2D tDiffuse;
    varying vec2 vUv;
    void main() {
      vec4 c = texture2D(tDiffuse, vUv);
      float l = dot(c.rgb, vec3(0.299, 0.587, 0.114));
      vec3 col = mix(vec3(l), c.rgb, 1.03);      // 채도 3% 올림
      col = (col - 0.5) * 1.04 + 0.5;            // 대비 4% 올림
      vec2 d = vUv - 0.5;
      col *= 1.0 - 0.45 * dot(d, d);             // 가장자리 살짝 어둡게
      gl_FragColor = vec4(clamp(col, 0.0, 1.0), c.a);
    }`,
};

export function createPost(renderer, scene, camera, Q) {
  if (!Q.post) return null;
  const size = renderer.getDrawingBufferSize(new THREE.Vector2());
  const target = new THREE.WebGLRenderTarget(size.x, size.y, { type: THREE.HalfFloatType, samples: Q.msaa });
  const composer = new EffectComposer(renderer, target);
  composer.addPass(new RenderPass(scene, camera));

  if (Q.ao) {
    const ao = new GTAOPass(scene, camera, size.x, size.y);
    ao.blendIntensity = 0.7;
    ao.updateGtaoMaterial({ radius: 1.5, distanceExponent: 1.5, thickness: 1, scale: 1 });
    // 셰이더로 위치를 바꾸는 잔디·잎·분수는 AO 계산용 그림에서 엉뚱한 곳에 그려지므로 빼고 계산한다
    const hide = ao.overrideVisibility.bind(ao);
    ao.overrideVisibility = () => {
      hide();
      scene.traverse((o) => {
        if (o.userData.noAO) o.visible = false;
      });
    };
    composer.addPass(ao);
  }
  composer.addPass(new UnrealBloomPass(new THREE.Vector2(size.x / 2, size.y / 2), 0.18, 0.4, 2.0));
  composer.addPass(new OutputPass());
  composer.addPass(new ShaderPass(GradeShader));

  return {
    render: () => composer.render(),
    setSize(w, h, pixelRatio) {
      composer.setPixelRatio(pixelRatio);
      composer.setSize(w, h);
    },
  };
}
