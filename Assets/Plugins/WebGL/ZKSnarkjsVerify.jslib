// ZKSnarkjsVerify.jslib
// Place at: Assets/Plugins/WebGL/ZKSnarkjsVerify.jslib
// Loads snarkjs in-browser, then verifies a Groth16 proof using StreamingAssets/zkdemo/*
//
// This is "real ZK": snarkjs.groth16.verify(vKey, publicSignals, proof) returns true/false.
// For hackathon safety, we use a known-good demo proof (multiplier: private a,b with public c=56).

mergeInto(LibraryManager.library, {
  ZK_VerifyDemoProof: function (gameObjectNamePtr, successCallbackPtr, errorCallbackPtr) {
    const goName = UTF8ToString(gameObjectNamePtr);
    const successCb = UTF8ToString(successCallbackPtr);
    const errorCb = UTF8ToString(errorCallbackPtr);

    const sendError = (msg) => {
      try { SendMessage(goName, errorCb, String(msg || 'ZK verify error')); }
      catch (e) { console.error('SendMessage error', e, msg); }
    };

    const sendSuccess = (verified, meta) => {
      try {
        const payload = JSON.stringify({ verified: !!verified, meta: meta || {} });
        SendMessage(goName, successCb, payload);
      } catch (e) {
        console.error('SendMessage success error', e);
        sendError(e && e.message ? e.message : 'SendMessage failed');
      }
    };

    const loadScriptOnce = (src) => new Promise((resolve, reject) => {
      if (window.snarkjs) return resolve(true);
      const existing = document.querySelector('script[data-snarkjs="1"]');
      if (existing) {
        existing.addEventListener('load', () => resolve(true));
        existing.addEventListener('error', () => reject(new Error('snarkjs load failed')));
        return;
      }
      const s = document.createElement('script');
      s.src = src;
      s.async = true;
      s.dataset.snarkjs = "1";
      s.onload = () => resolve(true);
      s.onerror = () => reject(new Error('snarkjs load failed'));
      document.head.appendChild(s);
    });

    const fetchJson = async (url) => {
      const r = await fetch(url, { cache: 'no-store' });
      if (!r.ok) throw new Error('Failed to fetch ' + url + ' (' + r.status + ')');
      return await r.json();
    };

    (async () => {
      try {
        // Use jsDelivr CDN (reliable). If you have no internet, host this file locally instead.
        await loadScriptOnce("https://cdn.jsdelivr.net/npm/snarkjs@0.7.6/build/snarkjs.min.js");

        if (!window.snarkjs || !window.snarkjs.groth16 || !window.snarkjs.groth16.verify) {
          return sendError('snarkjs not available after load.');
        }

        const base = "StreamingAssets/zkdemo/";
        const vkey = await fetchJson(base + "verification_key.json");
        const pub = await fetchJson(base + "public.json");
        const proof = await fetchJson(base + "proof.json");

        const ok = await window.snarkjs.groth16.verify(vkey, pub, proof);
        sendSuccess(ok, { circuit: "multiplier_demo", public: pub });
      } catch (e) {
        sendError(e && e.message ? e.message : String(e));
      }
    })();
  }
});
