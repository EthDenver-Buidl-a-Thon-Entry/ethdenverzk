// Metamask_WebGL.jslib
// Stable filename: OVERWRITE your existing Assets/Plugins/WebGL/Metamask_WebGL.jslib with this.
//
// Provides (names must match C# DllImport):
// - MetaMask_GetNativeBalance(go, ok, err)
// - MetaMask_SignMessage(message, go, ok, err)
// - MetaMask_SendNativeTransaction(to, valueWeiHex, go, ok, err)
// (Also keeps MetaMask_IsAvailable and MetaMask_ConnectAndSwitch)

mergeInto(LibraryManager.library, {
  MetaMask_IsAvailable: function () {
    try { return (typeof window !== 'undefined' && window.ethereum) ? 1 : 0; }
    catch (e) { return 0; }
  },

  MetaMask_ConnectAndSwitch: function (
    chainIdHexPtr,
    chainNamePtr,
    rpcUrlPtr,
    blockExplorerUrlPtr,
    nativeSymbolPtr,
    nativeDecimals,
    gameObjectNamePtr,
    successCallbackPtr,
    errorCallbackPtr
  ) {
    const chainIdHex = UTF8ToString(chainIdHexPtr);
    const chainName = UTF8ToString(chainNamePtr);
    const rpcUrl = UTF8ToString(rpcUrlPtr);
    const blockExplorerUrl = UTF8ToString(blockExplorerUrlPtr);
    const nativeSymbol = UTF8ToString(nativeSymbolPtr);
    const goName = UTF8ToString(gameObjectNamePtr);
    const successCb = UTF8ToString(successCallbackPtr);
    const errorCb = UTF8ToString(errorCallbackPtr);

    const sendError = (msg) => {
      try { SendMessage(goName, errorCb, String(msg || 'Wallet error')); }
      catch (e) { console.error('SendMessage error', e, msg); }
    };

    const sendSuccess = (address, chainId) => {
      try {
        const payload = JSON.stringify({ address: address, chainId: chainId });
        SendMessage(goName, successCb, payload);
      } catch (e) {
        console.error('SendMessage success error', e);
        sendError(e && e.message ? e.message : 'SendMessage failed');
      }
    };

    (async () => {
      try {
        const eth = (typeof window !== 'undefined') ? window.ethereum : null;
        if (!eth) return sendError('MetaMask not detected.');

        // try add chain (safe)
        try {
          await eth.request({
            method: 'wallet_addEthereumChain',
            params: [{
              chainId: chainIdHex,
              chainName: chainName,
              rpcUrls: [rpcUrl],
              nativeCurrency: { name: nativeSymbol, symbol: nativeSymbol, decimals: nativeDecimals },
              blockExplorerUrls: [blockExplorerUrl]
            }]
          });
        } catch (addErr) {
          if (addErr && addErr.code === 4001) throw addErr;
        }

        // switch
        try {
          await eth.request({ method: 'wallet_switchEthereumChain', params: [{ chainId: chainIdHex }] });
        } catch (swErr) {
          if (swErr && swErr.code === 4902) {
            await eth.request({
              method: 'wallet_addEthereumChain',
              params: [{
                chainId: chainIdHex,
                chainName: chainName,
                rpcUrls: [rpcUrl],
                nativeCurrency: { name: nativeSymbol, symbol: nativeSymbol, decimals: nativeDecimals },
                blockExplorerUrls: [blockExplorerUrl]
              }]
            });
            await eth.request({ method: 'wallet_switchEthereumChain', params: [{ chainId: chainIdHex }] });
          } else {
            throw swErr;
          }
        }

        const accounts = await eth.request({ method: 'eth_requestAccounts' });
        const address = (accounts && accounts.length) ? accounts[0] : '';
        const currentChainId = await eth.request({ method: 'eth_chainId' });

        if (!address) return sendError('No account returned from MetaMask.');
        sendSuccess(address, currentChainId);
      } catch (e) {
        sendError((e && e.message) ? e.message : String(e));
      }
    })();
  },

  MetaMask_GetNativeBalance: function(gameObjectNamePtr, successCallbackPtr, errorCallbackPtr) {
    const goName = UTF8ToString(gameObjectNamePtr);
    const successCb = UTF8ToString(successCallbackPtr);
    const errorCb = UTF8ToString(errorCallbackPtr);

    const sendError = (msg) => {
      try { SendMessage(goName, errorCb, String(msg || 'Balance error')); }
      catch (e) { console.error('SendMessage error', e, msg); }
    };

    const sendSuccess = (address, chainId, balanceHex) => {
      try {
        const payload = JSON.stringify({ address: address, chainId: chainId, balanceHex: balanceHex });
        SendMessage(goName, successCb, payload);
      } catch (e) {
        console.error('SendMessage success error', e);
        sendError(e && e.message ? e.message : 'SendMessage failed');
      }
    };

    (async () => {
      try {
        const eth = (typeof window !== 'undefined') ? window.ethereum : null;
        if (!eth) return sendError('MetaMask not detected.');

        let accounts = await eth.request({ method: 'eth_accounts' });
        if (!accounts || accounts.length === 0) accounts = await eth.request({ method: 'eth_requestAccounts' });
        const address = (accounts && accounts.length) ? accounts[0] : '';
        if (!address) return sendError('No connected account.');

        const chainId = await eth.request({ method: 'eth_chainId' });
        const balanceHex = await eth.request({ method: 'eth_getBalance', params: [address, 'latest'] });

        sendSuccess(address, chainId, balanceHex);
      } catch (e) {
        sendError((e && e.message) ? e.message : String(e));
      }
    })();
  },

  MetaMask_SignMessage: function(messagePtr, gameObjectNamePtr, successCallbackPtr, errorCallbackPtr) {
    const message = UTF8ToString(messagePtr);
    const goName = UTF8ToString(gameObjectNamePtr);
    const successCb = UTF8ToString(successCallbackPtr);
    const errorCb = UTF8ToString(errorCallbackPtr);

    const sendError = (msg) => {
      try { SendMessage(goName, errorCb, String(msg || 'Sign error')); }
      catch (e) { console.error('SendMessage error', e, msg); }
    };

    const sendSuccess = (address, signature) => {
      try {
        const payload = JSON.stringify({ address: address, signature: signature });
        SendMessage(goName, successCb, payload);
      } catch (e) {
        console.error('SendMessage success error', e);
        sendError(e && e.message ? e.message : 'SendMessage failed');
      }
    };

    (async () => {
      try {
        const eth = (typeof window !== 'undefined') ? window.ethereum : null;
        if (!eth) return sendError('MetaMask not detected.');

        let accounts = await eth.request({ method: 'eth_accounts' });
        if (!accounts || accounts.length === 0) accounts = await eth.request({ method: 'eth_requestAccounts' });
        const address = (accounts && accounts.length) ? accounts[0] : '';
        if (!address) return sendError('No connected account.');

        const signature = await eth.request({
          method: 'personal_sign',
          params: [message, address]
        });

        if (!signature) return sendError('No signature returned.');
        sendSuccess(address, signature);
      } catch (e) {
        sendError((e && e.message) ? e.message : String(e));
      }
    })();
  },

  MetaMask_SendNativeTransaction: function(toPtr, valueWeiHexPtr, gameObjectNamePtr, successCallbackPtr, errorCallbackPtr) {
    const to = UTF8ToString(toPtr);
    const valueWeiHex = UTF8ToString(valueWeiHexPtr);
    const goName = UTF8ToString(gameObjectNamePtr);
    const successCb = UTF8ToString(successCallbackPtr);
    const errorCb = UTF8ToString(errorCallbackPtr);

    const sendError = (msg) => {
      try { SendMessage(goName, errorCb, String(msg || 'Tx error')); }
      catch (e) { console.error('SendMessage error', e, msg); }
    };

    const sendSuccess = (from, txHash) => {
      try {
        const payload = JSON.stringify({ from: from, to: to, valueWeiHex: valueWeiHex, txHash: txHash });
        SendMessage(goName, successCb, payload);
      } catch (e) {
        console.error('SendMessage success error', e);
        sendError(e && e.message ? e.message : 'SendMessage failed');
      }
    };

    (async () => {
      try {
        const eth = (typeof window !== 'undefined') ? window.ethereum : null;
        if (!eth) return sendError('MetaMask not detected.');

        let accounts = await eth.request({ method: 'eth_accounts' });
        if (!accounts || accounts.length === 0) accounts = await eth.request({ method: 'eth_requestAccounts' });
        const from = (accounts && accounts.length) ? accounts[0] : '';
        if (!from) return sendError('No connected account.');

        const txHash = await eth.request({
          method: 'eth_sendTransaction',
          params: [{
            from: from,
            to: to,
            value: valueWeiHex
          }]
        });

        if (!txHash) return sendError('No tx hash returned.');
        sendSuccess(from, txHash);
      } catch (e) {
        sendError((e && e.message) ? e.message : String(e));
      }
    })();
  }
});
