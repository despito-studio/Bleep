import * as THREE from 'three';

export function createTapeEffect(renderer, scene, camera) {
  const target = new THREE.WebGLRenderTarget(640, 360, { minFilter: THREE.NearestFilter, magFilter: THREE.NearestFilter });
  const uniforms = {
    tScene: { value: target.texture }, time: { value: 0 }, grain: { value: 0.035 },
    scanlines: { value: 0.075 }, distortion: { value: 0.08 }, danger: { value: 0 },
    recording: { value: 0 }, aspect: { value: 1.777 }, nightmare: { value: 0 },
    insanity: { value: 0 },
  };
  const material = new THREE.ShaderMaterial({
    uniforms,
    vertexShader: `varying vec2 vUv; void main(){vUv=uv;gl_Position=vec4(position.xy,0.,1.);}`,
    fragmentShader: `
      uniform sampler2D tScene;
      uniform float time,grain,scanlines,distortion,danger,recording,aspect,nightmare,insanity;
      varying vec2 vUv;
      float noise(vec2 p){return fract(sin(dot(p,vec2(12.9898,78.233)))*43758.5453);}
      void main(){
        vec2 uv = vUv;
        vec2 p = uv * 2. - 1.;
        // Aspect-ratio-corrected coordinates for circular effects
        vec2 pAsp = vec2(p.x * aspect, p.y);

        // Barrel distortion
        float totalDistort = distortion + nightmare * 0.4 + insanity * 0.25;
        uv += p * dot(p, p) * 0.008 * totalDistort;

        // Nightmare world wave distortion
        if (nightmare > 0.05) {
          uv.x += sin(uv.y * 36. + time * 12.) * 0.006 * nightmare;
          uv.y += cos(uv.x * 28. + time * 9.) * 0.003 * nightmare;
        }

        // Glitch on danger or low sanity
        float glitchNoise = noise(vec2(floor(time * 10.), floor(uv.y * 24.)));
        float glitch = step(0.92, glitchNoise) * (danger * 0.8 + nightmare * 0.6 + insanity * 0.5);
        uv.x += sin(uv.y * 70. + time * 30.) * 0.02 * glitch;

        // Bottom VHS tracking jitter bar
        float trackBar = smoothstep(0.045, 0.015, uv.y) * step(0.002, uv.y);
        uv.x += (noise(vec2(time * 30., floor(uv.y * 120.))) - 0.5) * 0.04 * trackBar;

        // Chromatic aberration / color fringing
        float shift = 0.00065 + danger * 0.0025 + nightmare * 0.005 + insanity * 0.003;
        vec3 col = texture2D(tScene, uv).rgb;
        col.r = mix(col.r, texture2D(tScene, uv + vec2(shift, 0.)).r, totalDistort * 0.85);
        col.b = mix(col.b, texture2D(tScene, uv - vec2(shift, 0.)).b, totalDistort * 0.85);

        // VHS-C camcorder recording mode tint
        float luma = dot(col, vec3(0.299, 0.587, 0.114));
        col = mix(col, vec3(luma * 0.82, luma * 0.99, luma * 0.86), recording * 0.7);

        // Film grain
        float g = (noise(uv * vec2(1100., 740.) + fract(time) * 100.) - 0.5) * (grain + nightmare * 0.04 + insanity * 0.03);
        col += g;

        // Scanlines
        col *= 1. - sin(uv.y * 900.) * scanlines;

        // Vignette (circular, aspect corrected)
        col *= 1. - smoothstep(0.55, 1.85, length(pAsp)) * (0.58 + danger * 0.15);

        // Danger and Nightmare red pulse
        col.r += danger * 0.045;
        if (nightmare > 0.05) {
          col.r += (0.09 + sin(time * 4.) * 0.04) * nightmare;
          col.g *= 1. - nightmare * 0.25;
        }
        if (insanity > 0.3) {
          col.rgb = mix(col.rgb, vec3(luma), (insanity - 0.3) * 0.4);
        }

        gl_FragColor = vec4(col, 1.);
      }`,
    depthTest: false, depthWrite: false,
  });
  const quad = new THREE.Scene();
  quad.add(new THREE.Mesh(new THREE.PlaneGeometry(2, 2), material));
  const ortho = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1);
  return {
    uniforms,
    resize(w, h, quality = 360) {
      target.setSize(Math.round(quality * w / h), quality);
      uniforms.aspect.value = w / h;
    },
    render(time) {
      uniforms.time.value = time;
      renderer.setRenderTarget(target);
      renderer.render(scene, camera);
      renderer.setRenderTarget(null);
      renderer.render(quad, ortho);
    },
  };
}
