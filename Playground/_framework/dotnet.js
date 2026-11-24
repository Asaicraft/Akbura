//! Licensed to the .NET Foundation under one or more agreements.
//! The .NET Foundation licenses this file to you under the MIT license.

var e=!0;const t=async()=>WebAssembly.validate(new Uint8Array([0,97,115,109,1,0,0,0,1,4,1,96,0,0,3,2,1,0,10,8,1,6,0,6,64,25,11,11])),o=async()=>WebAssembly.validate(new Uint8Array([0,97,115,109,1,0,0,0,1,5,1,96,0,1,123,3,2,1,0,10,15,1,13,0,65,1,253,15,65,2,253,15,253,128,2,11])),n=async()=>WebAssembly.validate(new Uint8Array([0,97,115,109,1,0,0,0,1,5,1,96,0,1,123,3,2,1,0,10,10,1,8,0,65,0,253,15,253,98,11])),r=Symbol.for("wasm promise_control");function i(e,t){let o=null;const n=new Promise((function(n,r){o={isDone:!1,promise:null,resolve:t=>{o.isDone||(o.isDone=!0,n(t),e&&e())},reject:e=>{o.isDone||(o.isDone=!0,r(e),t&&t())}}}));o.promise=n;const i=n;return i[r]=o,{promise:i,promise_control:o}}function s(e){return e[r]}function a(e){e&&function(e){return void 0!==e[r]}(e)||qe(!1,"Promise is not controllable")}const l="__mono_message__",c=["debug","log","trace","warn","info","error"],d="MONO_WASM: ";let u,f,m,g;function p(e){g=e}function h(e){if(Ne.diagnosticTracing){const t="function"==typeof e?e():e;console.debug(d+t)}}function w(e,...t){console.info(d+e,...t)}function b(e,...t){console.info(e,...t)}function y(e,...t){console.warn(d+e,...t)}function v(e,...t){if(t&&t.length>0&&t[0]&&"object"==typeof t[0]){if(t[0].silent)return;if(t[0].toString)return void console.error(d+e,t[0].toString())}console.error(d+e,...t)}let _,E,T="",x=(new Date).valueOf();function j(t,o,n){return function(...r){try{let i=r[0];if(void 0===i)i="undefined";else if(null===i)i="null";else if("function"==typeof i)i=i.toString();else if("string"!=typeof i)try{i=JSON.stringify(i)}catch(e){i=i.toString()}if("string"==typeof i&&e){if(Pe&&-1!==i.indexOf("keeping the worker alive for asynchronous operation"))return;if(0===i.indexOf("MONO_WASM: ")||0===i.indexOf("[MONO]")){const e=new Date;x!==e.valueOf()&&(T=e.toISOString().substring(11,23),x=e.valueOf()),i=`[${g} ${T}] ${i}`}}o(n?JSON.stringify({method:t,payload:i,arguments:r.slice(1)}):[t+i,...r.slice(1)])}catch(e){m.error(`proxyConsole failed: ${e}`)}}}function R(e,t,o){f=t,g=e,m={...t};const n=`${o}/console`.replace("https://","wss://").replace("http://","ws://");u=new WebSocket(n),u.addEventListener("error",O),u.addEventListener("close",D),function(){for(const e of c)f[e]=j(`console.${e}`,A,!0)}()}function S(e){let t=30;const o=()=>{u?0==u.bufferedAmount||0==t?(e&&b(e),function(){for(const e of c)f[e]=j(`console.${e}`,m.log,!1)}(),u.removeEventListener("error",O),u.removeEventListener("close",D),u.close(1e3,e),u=void 0):(t--,globalThis.setTimeout(o,100)):e&&m&&m.log(e)};o()}function A(e){u&&u.readyState===WebSocket.OPEN?u.send(e):m.log(e)}function O(e){m.error(`[${g}] proxy console websocket error: ${e}`,e)}function D(e){m.debug(`[${g}] proxy console websocket closed: ${e}`,e)}function k(){Ne.preferredIcuAsset=I(Ne.config);let e="invariant"==Ne.config.globalizationMode;if(!e)if(Ne.preferredIcuAsset)Ne.diagnosticTracing&&h("ICU data archive(s) available, disabling invariant mode");else{if("custom"===Ne.config.globalizationMode||"all"===Ne.config.globalizationMode||"sharded"===Ne.config.globalizationMode){const e="invariant globalization mode is inactive and no ICU data archives are available";throw v(`ERROR: ${e}`),new Error(e)}Ne.diagnosticTracing&&h("ICU data archive(s) not available, using invariant globalization mode"),e=!0,Ne.preferredIcuAsset=null}const t="DOTNET_SYSTEM_GLOBALIZATION_INVARIANT",o=Ne.config.environmentVariables;if(void 0===o[t]&&e&&(o[t]="1"),void 0===o.TZ)try{const e=Intl.DateTimeFormat().resolvedOptions().timeZone||null;e&&(o.TZ=e)}catch(e){w("failed to detect timezone, will fallback to UTC")}}function I(e){var t;if((null===(t=e.resources)||void 0===t?void 0:t.icu)&&"invariant"!=e.globalizationMode){const t=e.applicationCulture||(Ce?globalThis.navigator&&globalThis.navigator.languages&&globalThis.navigator.languages[0]:Intl.DateTimeFormat().resolvedOptions().locale),o=e.resources.icu;let n=null;if("custom"===e.globalizationMode){if(o.length>=1)return o[0].name}else t&&"all"!==e.globalizationMode?"sharded"===e.globalizationMode&&(n=function(e){const t=e.split("-")[0];return"en"===t||["fr","fr-FR","it","it-IT","de","de-DE","es","es-ES"].includes(e)?"icudt_EFIGS.dat":["zh","ko","ja"].includes(t)?"icudt_CJK.dat":"icudt_no_CJK.dat"}(t)):n="icudt.dat";if(n)for(let e=0;e<o.length;e++){const t=o[e];if(t.virtualPath===n)return t.name}}return e.globalizationMode="invariant",null}const P=class{constructor(e){this.url=e}toString(){return this.url}};async function C(e,t){try{const o="function"==typeof globalThis.fetch;if(De){const n=e.startsWith("file://");if(!n&&o)return globalThis.fetch(e,t||{credentials:"same-origin"});_||(E=ze.require("url"),_=ze.require("fs")),n&&(e=E.fileURLToPath(e));const r=await _.promises.readFile(e);return{ok:!0,headers:{length:0,get:()=>null},url:e,arrayBuffer:()=>r,json:()=>JSON.parse(r),text:()=>{throw new Error("NotImplementedException")}}}if(o)return globalThis.fetch(e,t||{credentials:"same-origin"});if("function"==typeof read)return{ok:!0,url:e,headers:{length:0,get:()=>null},arrayBuffer:()=>new Uint8Array(read(e,"binary")),json:()=>JSON.parse(read(e,"utf8")),text:()=>read(e,"utf8")}}catch(t){return{ok:!1,url:e,status:500,headers:{length:0,get:()=>null},statusText:"ERR28: "+t,arrayBuffer:()=>{throw t},json:()=>{throw t},text:()=>{throw t}}}throw new Error("No fetch implementation available")}function M(e){return"string"!=typeof e&&qe(!1,"url must be a string"),!L(e)&&0!==e.indexOf("./")&&0!==e.indexOf("../")&&globalThis.URL&&globalThis.document&&globalThis.document.baseURI&&(e=new URL(e,globalThis.document.baseURI).toString()),e}const U=/^[a-zA-Z][a-zA-Z\d+\-.]*?:\/\//,N=/[a-zA-Z]:[\\/]/;function L(e){return De||Me?e.startsWith("/")||e.startsWith("\\")||-1!==e.indexOf("///")||N.test(e):U.test(e)}let $,z=0;const W=[],F=[],B=new Map,V={"js-module-threads":!0,"js-module-runtime":!0,"js-module-dotnet":!0,"js-module-native":!0,"js-module-diagnostics":!0},q={...V,"js-module-library-initializer":!0},J={...V,dotnetwasm:!0,heap:!0,manifest:!0},H={...q,manifest:!0},Z={...q,dotnetwasm:!0},Q={dotnetwasm:!0,symbols:!0},G={...q,dotnetwasm:!0,symbols:!0},K={symbols:!0};function X(e){return!("icu"==e.behavior&&e.name!=Ne.preferredIcuAsset)}function Y(e,t,o){null!=t||(t=[]),qe(1==t.length,`Expect to have one ${o} asset in resources`);const n=t[0];return n.behavior=o,ee(n),e.push(n),n}function ee(e){J[e.behavior]&&B.set(e.behavior,e)}function te(e){qe(J[e],`Unknown single asset behavior ${e}`);const t=B.get(e);if(t&&!t.resolvedUrl)if(t.resolvedUrl=Ne.locateFile(t.name),V[t.behavior]){const e=he(t);e?("string"!=typeof e&&qe(!1,"loadBootResource response for 'dotnetjs' type should be a URL string"),t.resolvedUrl=e):t.resolvedUrl=ue(t.resolvedUrl,t.behavior)}else if("dotnetwasm"!==t.behavior)throw new Error(`Unknown single asset behavior ${e}`);return t}function oe(e){const t=te(e);return qe(t,`Single asset for ${e} not found`),t}let ne=!1;async function re(){if(!ne){ne=!0,Ne.diagnosticTracing&&h("mono_download_assets");try{const e=[],t=[],o=(e,t)=>{!G[e.behavior]&&X(e)&&Ne.expected_instantiated_assets_count++,!Z[e.behavior]&&X(e)&&(Ne.expected_downloaded_assets_count++,t.push(le(e)))};for(const t of W)o(t,e);for(const e of F)o(e,t);Ne.allDownloadsQueued.promise_control.resolve(),Promise.all([...e,...t]).then((()=>{Ne.allDownloadsFinished.promise_control.resolve()})).catch((e=>{throw Ne.err("Error in mono_download_assets: "+e),et(1,e),e})),await Ne.runtimeModuleLoaded.promise;const n=async e=>{const t=await e;if(t.buffer){if(!G[t.behavior]){t.buffer&&"object"==typeof t.buffer||qe(!1,"asset buffer must be array-like or buffer-like or promise of these"),"string"!=typeof t.resolvedUrl&&qe(!1,"resolvedUrl must be string");const e=t.resolvedUrl,o=await t.buffer,n=new Uint8Array(o);we(t),await Ue.beforeOnRuntimeInitialized.promise,Ue.instantiate_asset(t,e,n)}}else Q[t.behavior]?("symbols"===t.behavior&&(await Ue.instantiate_symbols_asset(t),we(t)),Q[t.behavior]&&++Ne.actual_downloaded_assets_count):(t.isOptional||qe(!1,"Expected asset to have the downloaded buffer"),!Z[t.behavior]&&X(t)&&Ne.expected_downloaded_assets_count--,!G[t.behavior]&&X(t)&&Ne.expected_instantiated_assets_count--)},r=[],i=[];for(const t of e)r.push(n(t));for(const e of t)i.push(n(e));Promise.all(r).then((()=>{Pe||Ue.coreAssetsInMemory.promise_control.resolve()})).catch((e=>{throw Ne.err("Error in mono_download_assets: "+e),et(1,e),e})),Promise.all(i).then((async()=>{Pe||(await Ue.coreAssetsInMemory.promise,Ue.allAssetsInMemory.promise_control.resolve())})).catch((e=>{throw Ne.err("Error in mono_download_assets: "+e),et(1,e),e}))}catch(e){throw Ne.err("Error in mono_download_assets: "+e),e}}}let ie=!1;function se(){if(ie)return;ie=!0;const e=Ne.config,t=[];if(e.assets)for(const t of e.assets)"object"!=typeof t&&qe(!1,`asset must be object, it was ${typeof t} : ${t}`),"string"!=typeof t.behavior&&qe(!1,"asset behavior must be known string"),"string"!=typeof t.name&&qe(!1,"asset name must be string"),t.resolvedUrl&&"string"!=typeof t.resolvedUrl&&qe(!1,"asset resolvedUrl could be string"),t.hash&&"string"!=typeof t.hash&&qe(!1,"asset resolvedUrl could be string"),t.pendingDownload&&"object"!=typeof t.pendingDownload&&qe(!1,"asset pendingDownload could be object"),t.isCore?W.push(t):F.push(t),ee(t);else if(e.resources){const o=e.resources;o.wasmNative||qe(!1,"resources.wasmNative must be defined"),o.jsModuleNative||qe(!1,"resources.jsModuleNative must be defined"),o.jsModuleRuntime||qe(!1,"resources.jsModuleRuntime must be defined"),o.jsModuleWorker||qe(!1,"resources.jsModuleWorker must be defined"),Y(F,o.wasmNative,"dotnetwasm"),Y(t,o.jsModuleNative,"js-module-native"),Y(t,o.jsModuleRuntime,"js-module-runtime"),o.jsModuleDiagnostics&&Y(t,o.jsModuleDiagnostics,"js-module-diagnostics"),Y(t,o.jsModuleWorker,"js-module-threads");const n=(e,t,o)=>{const n=e;n.behavior=t,o?(n.isCore=!0,W.push(n)):F.push(n)};if(o.coreAssembly)for(let e=0;e<o.coreAssembly.length;e++)n(o.coreAssembly[e],"assembly",!0);if(o.assembly)for(let e=0;e<o.assembly.length;e++)n(o.assembly[e],"assembly",!o.coreAssembly);if(0!=e.debugLevel&&Ne.isDebuggingSupported()){if(o.corePdb)for(let e=0;e<o.corePdb.length;e++)n(o.corePdb[e],"pdb",!0);if(o.pdb)for(let e=0;e<o.pdb.length;e++)n(o.pdb[e],"pdb",!o.corePdb)}if(e.loadAllSatelliteResources&&o.satelliteResources)for(const e in o.satelliteResources)for(let t=0;t<o.satelliteResources[e].length;t++){const r=o.satelliteResources[e][t];r.culture=e,n(r,"resource",!o.coreAssembly)}if(o.coreVfs)for(let e=0;e<o.coreVfs.length;e++)n(o.coreVfs[e],"vfs",!0);if(o.vfs)for(let e=0;e<o.vfs.length;e++)n(o.vfs[e],"vfs",!o.coreVfs);const r=I(e);if(r&&o.icu)for(let e=0;e<o.icu.length;e++){const t=o.icu[e];t.name===r&&n(t,"icu",!1)}if(o.wasmSymbols)for(let e=0;e<o.wasmSymbols.length;e++)n(o.wasmSymbols[e],"symbols",!1)}if(e.appsettings)for(let t=0;t<e.appsettings.length;t++){const o=e.appsettings[t],n=be(o);"appsettings.json"!==n&&n!==`appsettings.${e.applicationEnvironment}.json`||F.push({name:o,behavior:"vfs",cache:"no-cache",useCredentials:!0})}e.assets=[...W,...F,...t]}async function ae(e){const t=await le(e);return await t.pendingDownloadInternal.response,t.buffer}async function le(e){try{return await ce(e)}catch(t){if(!Ne.enableDownloadRetry)throw t;if(Me||De)throw t;if(e.pendingDownload&&e.pendingDownloadInternal==e.pendingDownload)throw t;if(e.resolvedUrl&&-1!=e.resolvedUrl.indexOf("file://"))throw t;if(t&&404==t.status)throw t;e.pendingDownloadInternal=void 0,await Ne.allDownloadsQueued.promise;try{return Ne.diagnosticTracing&&h(`Retrying download '${e.name}'`),await ce(e)}catch(t){return e.pendingDownloadInternal=void 0,await new Promise((e=>globalThis.setTimeout(e,100))),Ne.diagnosticTracing&&h(`Retrying download (2) '${e.name}' after delay`),await ce(e)}}}async function ce(e){for(;$;)await $.promise;try{++z,z==Ne.maxParallelDownloads&&(Ne.diagnosticTracing&&h("Throttling further parallel downloads"),$=i());const t=await async function(e){if(e.pendingDownload&&(e.pendingDownloadInternal=e.pendingDownload),e.pendingDownloadInternal&&e.pendingDownloadInternal.response)return e.pendingDownloadInternal.response;if(e.buffer){const t=await e.buffer;return e.resolvedUrl||(e.resolvedUrl="undefined://"+e.name),e.pendingDownloadInternal={url:e.resolvedUrl,name:e.name,response:Promise.resolve({ok:!0,arrayBuffer:()=>t,json:()=>JSON.parse(new TextDecoder("utf-8").decode(t)),text:()=>{throw new Error("NotImplementedException")},headers:{get:()=>{}}})},e.pendingDownloadInternal.response}const t=e.loadRemote&&Ne.config.remoteSources?Ne.config.remoteSources:[""];let o;for(let n of t){n=n.trim(),"./"===n&&(n="");const t=de(e,n);e.name===t?Ne.diagnosticTracing&&h(`Attempting to download '${t}'`):Ne.diagnosticTracing&&h(`Attempting to download '${t}' for ${e.name}`);try{e.resolvedUrl=t;const n=ge(e);if(e.pendingDownloadInternal=n,o=await n.response,!o||!o.ok)continue;return o}catch(e){o||(o={ok:!1,url:t,status:0,statusText:""+e});continue}}const n=e.isOptional||e.name.match(/\.pdb$/)&&Ne.config.ignorePdbLoadErrors;if(o||qe(!1,`Response undefined ${e.name}`),!n){const t=new Error(`download '${o.url}' for ${e.name} failed ${o.status} ${o.statusText}`);throw t.status=o.status,t}w(`optional download '${o.url}' for ${e.name} failed ${o.status} ${o.statusText}`)}(e);return t?(Q[e.behavior]||(e.buffer=await t.arrayBuffer(),++Ne.actual_downloaded_assets_count),e):e}finally{if(--z,$&&z==Ne.maxParallelDownloads-1){Ne.diagnosticTracing&&h("Resuming more parallel downloads");const e=$;$=void 0,e.promise_control.resolve()}}}function de(e,t){let o;return null==t&&qe(!1,`sourcePrefix must be provided for ${e.name}`),e.resolvedUrl?o=e.resolvedUrl:(o=""===t?"assembly"===e.behavior||"pdb"===e.behavior?e.name:"resource"===e.behavior&&e.culture&&""!==e.culture?`${e.culture}/${e.name}`:e.name:t+e.name,o=ue(Ne.locateFile(o),e.behavior)),o&&"string"==typeof o||qe(!1,"attemptUrl need to be path or url string"),o}function ue(e,t){return Ne.modulesUniqueQuery&&H[t]&&(e+=Ne.modulesUniqueQuery),e}let fe=0;const me=new Set;function ge(e){try{e.resolvedUrl||qe(!1,"Request's resolvedUrl must be set");const t=function(e){let t=e.resolvedUrl;if(Ne.loadBootResource){const o=he(e);if(o instanceof Promise)return o;"string"==typeof o&&(t=o)}const o={};return e.cache?o.cache=e.cache:Ne.config.disableNoCacheFetch||(o.cache="no-cache"),e.useCredentials?o.credentials="include":!Ne.config.disableIntegrityCheck&&e.hash&&(o.integrity=e.hash),Ne.fetch_like(t,o)}(e),o={name:e.name,url:e.resolvedUrl,response:t};return me.add(e.name),o.response.then((()=>{"assembly"==e.behavior&&Ne.loadedAssemblies.push(e.name),fe++,Ne.onDownloadResourceProgress&&Ne.onDownloadResourceProgress(fe,me.size)})),o}catch(t){const o={ok:!1,url:e.resolvedUrl,status:500,statusText:"ERR29: "+t,arrayBuffer:()=>{throw t},json:()=>{throw t}};return{name:e.name,url:e.resolvedUrl,response:Promise.resolve(o)}}}const pe={resource:"assembly",assembly:"assembly",pdb:"pdb",icu:"globalization",vfs:"configuration",manifest:"manifest",dotnetwasm:"dotnetwasm","js-module-dotnet":"dotnetjs","js-module-native":"dotnetjs","js-module-runtime":"dotnetjs","js-module-threads":"dotnetjs"};function he(e){var t;if(Ne.loadBootResource){const o=null!==(t=e.hash)&&void 0!==t?t:"",n=e.resolvedUrl,r=pe[e.behavior];if(r){const t=Ne.loadBootResource(r,e.name,n,o,e.behavior);return"string"==typeof t?M(t):t}}}function we(e){e.pendingDownloadInternal=null,e.pendingDownload=null,e.buffer=null,e.moduleExports=null}function be(e){let t=e.lastIndexOf("/");return t>=0&&t++,e.substring(t)}async function ye(e){e&&await Promise.all((null!=e?e:[]).map((e=>async function(e){try{const t=e.name;if(!e.moduleExports){const o=ue(Ne.locateFile(t),"js-module-library-initializer");Ne.diagnosticTracing&&h(`Attempting to import '${o}' for ${e}`),e.moduleExports=await import(/*! webpackIgnore: true */o)}Ne.libraryInitializers.push({scriptName:t,exports:e.moduleExports})}catch(t){y(`Failed to import library initializer '${e}': ${t}`)}}(e))))}async function ve(e,t){if(!Ne.libraryInitializers)return;const o=[];for(let n=0;n<Ne.libraryInitializers.length;n++){const r=Ne.libraryInitializers[n];r.exports[e]&&o.push(_e(r.scriptName,e,(()=>r.exports[e](...t))))}await Promise.all(o)}async function _e(e,t,o){try{await o()}catch(o){throw y(`Failed to invoke '${t}' on library initializer '${e}': ${o}`),et(1,o),o}}function Ee(e,t){if(e===t)return e;const o={...t};return void 0!==o.assets&&o.assets!==e.assets&&(o.assets=[...e.assets||[],...o.assets||[]]),void 0!==o.resources&&(o.resources=xe(e.resources||{assembly:[],jsModuleNative:[],jsModuleRuntime:[],wasmNative:[]},o.resources)),void 0!==o.environmentVariables&&(o.environmentVariables={...e.environmentVariables||{},...o.environmentVariables||{}}),void 0!==o.runtimeOptions&&o.runtimeOptions!==e.runtimeOptions&&(o.runtimeOptions=[...e.runtimeOptions||[],...o.runtimeOptions||[]]),Object.assign(e,o)}function Te(e,t){if(e===t)return e;const o={...t};return o.config&&(e.config||(e.config={}),o.config=Ee(e.config,o.config)),Object.assign(e,o)}function xe(e,t){if(e===t)return e;const o={...t};return void 0!==o.coreAssembly&&(o.coreAssembly=[...e.coreAssembly||[],...o.coreAssembly||[]]),void 0!==o.assembly&&(o.assembly=[...e.assembly||[],...o.assembly||[]]),void 0!==o.lazyAssembly&&(o.lazyAssembly=[...e.lazyAssembly||[],...o.lazyAssembly||[]]),void 0!==o.corePdb&&(o.corePdb=[...e.corePdb||[],...o.corePdb||[]]),void 0!==o.pdb&&(o.pdb=[...e.pdb||[],...o.pdb||[]]),void 0!==o.jsModuleWorker&&(o.jsModuleWorker=[...e.jsModuleWorker||[],...o.jsModuleWorker||[]]),void 0!==o.jsModuleNative&&(o.jsModuleNative=[...e.jsModuleNative||[],...o.jsModuleNative||[]]),void 0!==o.jsModuleDiagnostics&&(o.jsModuleDiagnostics=[...e.jsModuleDiagnostics||[],...o.jsModuleDiagnostics||[]]),void 0!==o.jsModuleRuntime&&(o.jsModuleRuntime=[...e.jsModuleRuntime||[],...o.jsModuleRuntime||[]]),void 0!==o.wasmSymbols&&(o.wasmSymbols=[...e.wasmSymbols||[],...o.wasmSymbols||[]]),void 0!==o.wasmNative&&(o.wasmNative=[...e.wasmNative||[],...o.wasmNative||[]]),void 0!==o.icu&&(o.icu=[...e.icu||[],...o.icu||[]]),void 0!==o.satelliteResources&&(o.satelliteResources=function(e,t){if(e===t)return e;for(const o in t)e[o]=[...e[o]||[],...t[o]||[]];return e}(e.satelliteResources||{},o.satelliteResources||{})),void 0!==o.modulesAfterConfigLoaded&&(o.modulesAfterConfigLoaded=[...e.modulesAfterConfigLoaded||[],...o.modulesAfterConfigLoaded||[]]),void 0!==o.modulesAfterRuntimeReady&&(o.modulesAfterRuntimeReady=[...e.modulesAfterRuntimeReady||[],...o.modulesAfterRuntimeReady||[]]),void 0!==o.extensions&&(o.extensions={...e.extensions||{},...o.extensions||{}}),void 0!==o.vfs&&(o.vfs=[...e.vfs||[],...o.vfs||[]]),Object.assign(e,o)}function je(){const e=Ne.config;if(e.environmentVariables=e.environmentVariables||{},e.runtimeOptions=e.runtimeOptions||[],e.resources=e.resources||{assembly:[],jsModuleNative:[],jsModuleWorker:[],jsModuleRuntime:[],wasmNative:[],vfs:[],satelliteResources:{}},e.assets){Ne.diagnosticTracing&&h("config.assets is deprecated, use config.resources instead");for(const t of e.assets){const o={};switch(t.behavior){case"assembly":o.assembly=[t];break;case"pdb":o.pdb=[t];break;case"resource":o.satelliteResources={},o.satelliteResources[t.culture]=[t];break;case"icu":o.icu=[t];break;case"symbols":o.wasmSymbols=[t];break;case"vfs":o.vfs=[t];break;case"dotnetwasm":o.wasmNative=[t];break;case"js-module-threads":o.jsModuleWorker=[t];break;case"js-module-runtime":o.jsModuleRuntime=[t];break;case"js-module-native":o.jsModuleNative=[t];break;case"js-module-diagnostics":o.jsModuleDiagnostics=[t];break;case"js-module-dotnet":break;default:throw new Error(`Unexpected behavior ${t.behavior} of asset ${t.name}`)}xe(e.resources,o)}}e.debugLevel,e.applicationEnvironment||(e.applicationEnvironment="Production"),Number.isInteger(e.pthreadPoolInitialSize)||(e.pthreadPoolInitialSize=5),Number.isInteger(e.pthreadPoolUnusedSize)||(e.pthreadPoolUnusedSize=1),null==e.jsThreadBlockingMode&&(e.jsThreadBlockingMode="PreventSynchronousJSExport"),void 0===e.environmentVariables.MONO_SLEEP_ABORT_LIMIT&&(e.environmentVariables.MONO_SLEEP_ABORT_LIMIT="5000"),e.applicationCulture&&(e.environmentVariables.LANG=`${e.applicationCulture}.UTF-8`),Ue.diagnosticTracing=Ne.diagnosticTracing=!!e.diagnosticTracing,Ue.waitForDebugger=e.waitForDebugger,Ne.maxParallelDownloads=e.maxParallelDownloads||Ne.maxParallelDownloads,Ne.enableDownloadRetry=void 0!==e.enableDownloadRetry?e.enableDownloadRetry:Ne.enableDownloadRetry}let Re=!1;async function Se(e){var t;if(Re)return void await Ne.afterConfigLoaded.promise;let o;try{if(e.configSrc||Ne.config&&0!==Object.keys(Ne.config).length&&(Ne.config.assets||Ne.config.resources)||(e.configSrc="dotnet.boot.js"),o=e.configSrc,Re=!0,o&&(Ne.diagnosticTracing&&h("mono_wasm_load_config"),await async function(e){const t=e.configSrc,o=Ne.locateFile(t);let n=null;void 0!==Ne.loadBootResource&&(n=Ne.loadBootResource("manifest",t,o,"","manifest"));let r,i=null;if(n)if("string"==typeof n)n.includes(".json")?(i=await s(M(n)),r=await Oe(i)):r=(await import(M(n))).config;else{const e=await n;"function"==typeof e.json?(i=e,r=await Oe(i)):r=e.config}else o.includes(".json")?(i=await s(ue(o,"manifest")),r=await Oe(i)):r=(await import(ue(o,"manifest"))).config;function s(e){return Ne.fetch_like(e,{method:"GET",credentials:"include",cache:"no-cache"})}Ne.config.applicationEnvironment&&(r.applicationEnvironment=Ne.config.applicationEnvironment),Ee(Ne.config,r)}(e)),je(),await ye(null===(t=Ne.config.resources)||void 0===t?void 0:t.modulesAfterConfigLoaded),await ve("onRuntimeConfigLoaded",[Ne.config]),e.onConfigLoaded)try{await e.onConfigLoaded(Ne.config,$e),je()}catch(e){throw v("onConfigLoaded() failed",e),e}je(),Ne.afterConfigLoaded.promise_control.resolve(Ne.config)}catch(t){const n=`Failed to load config file ${o} ${t} ${null==t?void 0:t.stack}`;throw Ne.config=e.config=Object.assign(Ne.config,{message:n,error:t,isError:!0}),et(1,new Error(n)),t}}function Ae(){return!!globalThis.navigator&&(Ne.isChromium||Ne.isFirefox)}async function Oe(e){const t=Ne.config,o=await e.json();t.applicationEnvironment||o.applicationEnvironment||(o.applicationEnvironment=e.headers.get("Blazor-Environment")||e.headers.get("DotNet-Environment")||void 0),o.environmentVariables||(o.environmentVariables={});const n=e.headers.get("DOTNET-MODIFIABLE-ASSEMBLIES");n&&(o.environmentVariables.DOTNET_MODIFIABLE_ASSEMBLIES=n);const r=e.headers.get("ASPNETCORE-BROWSER-TOOLS");return r&&(o.environmentVariables.__ASPNETCORE_BROWSER_TOOLS=r),o}"function"!=typeof importScripts||globalThis.onmessage||(globalThis.dotnetSidecar=!0);const De="object"==typeof process&&"object"==typeof process.versions&&"string"==typeof process.versions.node,ke="function"==typeof importScripts,Ie=ke&&"undefined"!=typeof dotnetSidecar,Pe=ke&&!Ie,Ce="object"==typeof window||ke&&!De,Me=!Ce&&!De;let Ue={},Ne={},Le={},$e={},ze={},We=!1;const Fe={},Be={config:Fe},Ve={mono:{},binding:{},internal:ze,module:Be,loaderHelpers:Ne,runtimeHelpers:Ue,diagnosticHelpers:Le,api:$e};function qe(e,t){if(e)return;const o="Assert failed: "+("function"==typeof t?t():t),n=new Error(o);v(o,n),Ue.nativeAbort(n)}function Je(){return void 0!==Ne.exitCode}function He(){return Ue.runtimeReady&&!Je()}function Ze(){Je()&&qe(!1,`.NET runtime already exited with ${Ne.exitCode} ${Ne.exitReason}. You can use runtime.runMain() which doesn't exit the runtime.`),Pe?Ue.runtimeReady||qe(!1,"The WebWorker is not attached to the runtime. See https://github.com/dotnet/runtime/blob/main/src/mono/wasm/threads.md#JS-interop-on-dedicated-threads"):Ue.runtimeReady||qe(!1,".NET runtime didn't start yet. Please call dotnet.create() first.")}function Qe(){Ce&&(globalThis.addEventListener("unhandledrejection",ot),globalThis.addEventListener("error",nt))}let Ge,Ke;function Xe(e){Ke&&Ke(e),et(e,Ne.exitReason)}function Ye(e){var t;if(Ge&&Ge(e||Ne.exitReason),(null===(t=Ne.config)||void 0===t?void 0:t.dumpThreadsOnNonZeroExit)&&Ue.mono_wasm_print_thread_dump&&void 0===Ne.exitCode)try{Ue.mono_wasm_print_thread_dump()}catch(e){}et(1,e||Ne.exitReason)}function et(t,o){var n,r;const i=o&&"object"==typeof o;t=i&&"number"==typeof o.status?o.status:void 0===t?-1:t;const s=i&&"string"==typeof o.message?o.message:""+o;(o=i?o:Ue.ExitStatus?function(e,t){const o=new Ue.ExitStatus(e);return o.message=t,o.toString=()=>t,o}(t,s):new Error("Exit with code "+t+" "+s)).status=t,o.message||(o.message=s);const a=""+(o.stack||(new Error).stack);try{Object.defineProperty(o,"stack",{get:()=>a})}catch(e){}const l=!!o.silent;if(o.silent=!0,Je())Ne.diagnosticTracing&&h("mono_exit called after exit");else{try{Be.onAbort==Ye&&(Be.onAbort=Ge),Be.onExit==Xe&&(Be.onExit=Ke),Ce&&(globalThis.removeEventListener("unhandledrejection",ot),globalThis.removeEventListener("error",nt)),Ue.runtimeReady?(Ue.jiterpreter_dump_stats&&Ue.jiterpreter_dump_stats(!1),0===t&&(null===(n=Ne.config)||void 0===n?void 0:n.interopCleanupOnExit)&&Ue.forceDisposeProxies(!0,!0),e&&0!==t&&(null===(r=Ne.config)||void 0===r?void 0:r.dumpThreadsOnNonZeroExit)&&Ue.dumpThreads()):(Ne.diagnosticTracing&&h(`abort_startup, reason: ${o}`),function(e){Ne.allDownloadsQueued.promise_control.reject(e),Ne.allDownloadsFinished.promise_control.reject(e),Ne.afterConfigLoaded.promise_control.reject(e),Ne.wasmCompilePromise.promise_control.reject(e),Ne.runtimeModuleLoaded.promise_control.reject(e),Ue.dotnetReady&&(Ue.dotnetReady.promise_control.reject(e),Ue.afterInstantiateWasm.promise_control.reject(e),Ue.beforePreInit.promise_control.reject(e),Ue.afterPreInit.promise_control.reject(e),Ue.afterPreRun.promise_control.reject(e),Ue.beforeOnRuntimeInitialized.promise_control.reject(e),Ue.afterOnRuntimeInitialized.promise_control.reject(e),Ue.afterPostRun.promise_control.reject(e))}(o))}catch(e){y("mono_exit A failed",e)}try{l||(function(e,t){if(0!==e&&t){const e=Ue.ExitStatus&&t instanceof Ue.ExitStatus?h:v;"string"==typeof t?e(t):(void 0===t.stack&&(t.stack=(new Error).stack+""),t.message?e(Ue.stringify_as_error_with_stack?Ue.stringify_as_error_with_stack(t.message+"\n"+t.stack):t.message+"\n"+t.stack):e(JSON.stringify(t)))}!Pe&&Ne.config&&(Ne.config.logExitCode?Ne.config.forwardConsoleLogsToWS?S("WASM EXIT "+e):b("WASM EXIT "+e):Ne.config.forwardConsoleLogsToWS&&S())}(t,o),function(e){if(Ce&&!Pe&&Ne.config&&Ne.config.appendElementOnExit&&document){const t=document.createElement("label");t.id="tests_done",0!==e&&(t.style.background="red"),t.innerHTML=""+e,document.body.appendChild(t)}}(t))}catch(e){y("mono_exit B failed",e)}Ne.exitCode=t,Ne.exitReason||(Ne.exitReason=o),!Pe&&Ue.runtimeReady&&Be.runtimeKeepalivePop()}if(Ne.config&&Ne.config.asyncFlushOnExit&&0===t)throw(async()=>{try{await async function(){try{const e=await import(/*! webpackIgnore: true */"process"),t=e=>new Promise(((t,o)=>{e.on("error",o),e.end("","utf8",t)})),o=t(e.stderr),n=t(e.stdout);let r;const i=new Promise((e=>{r=setTimeout((()=>e("timeout")),1e3)}));await Promise.race([Promise.all([n,o]),i]),clearTimeout(r)}catch(e){v(`flushing std* streams failed: ${e}`)}}()}finally{tt(t,o)}})(),o;tt(t,o)}function tt(e,t){if(Pe&&Ue.runtimeReady&&Ue.nativeAbort)throw Ue.nativeAbort(t),t;if(Ue.runtimeReady&&Ue.nativeExit)try{Ue.nativeExit(e)}catch(e){!Ue.ExitStatus||e instanceof Ue.ExitStatus||y("set_exit_code_and_quit_now failed: "+e.toString())}if(0!==e||!Ce)throw De&&ze.process?ze.process.exit(e):Ue.quit&&Ue.quit(e,t),t}function ot(e){rt(e,e.reason,"rejection")}function nt(e){rt(e,e.error,"error")}function rt(e,t,o){e.preventDefault();try{t||(t=new Error("Unhandled "+o)),void 0===t.stack&&(t.stack=(new Error).stack),t.stack=t.stack+"",t.silent||(v("Unhandled error:",t),et(1,t))}catch(e){}}!function(e){if(We)throw new Error("Loader module already loaded");We=!0,Ue=e.runtimeHelpers,Ne=e.loaderHelpers,Le=e.diagnosticHelpers,$e=e.api,ze=e.internal,Object.assign($e,{INTERNAL:ze,invokeLibraryInitializers:ve}),Object.assign(e.module,{config:Ee(Fe,{environmentVariables:{}})});const r={mono_wasm_bindings_is_ready:!1,config:e.module.config,diagnosticTracing:!1,nativeAbort:e=>{throw e||new Error("abort")},nativeExit:e=>{throw new Error("exit:"+e)}},l={gitHash:"8e0f8ed2312954727f8919d7d1c2f78b483b4ee7",config:e.module.config,diagnosticTracing:!1,maxParallelDownloads:16,enableDownloadRetry:!0,_loaded_files:[],loadedFiles:[],loadedAssemblies:[],libraryInitializers:[],workerNextNumber:1,actual_downloaded_assets_count:0,actual_instantiated_assets_count:0,expected_downloaded_assets_count:0,expected_instantiated_assets_count:0,afterConfigLoaded:i(),allDownloadsQueued:i(),allDownloadsFinished:i(),wasmCompilePromise:i(),runtimeModuleLoaded:i(),loadingWorkers:i(),is_exited:Je,is_runtime_running:He,assert_runtime_running:Ze,mono_exit:et,createPromiseController:i,getPromiseController:s,assertIsControllablePromise:a,mono_download_assets:re,resolve_single_asset_path:oe,setup_proxy_console:R,set_thread_prefix:p,installUnhandledErrorHandler:Qe,retrieve_asset_download:ae,invokeLibraryInitializers:ve,isDebuggingSupported:Ae,exceptions:t,simd:n,relaxedSimd:o};Object.assign(Ue,r),Object.assign(Ne,l)}(Ve);let it,st,at,lt=!1,ct=!1;async function dt(e){if(!ct){if(ct=!0,Ce&&Ne.config.forwardConsoleLogsToWS&&void 0!==globalThis.WebSocket&&R("main",globalThis.console,globalThis.location.origin),Be||qe(!1,"Null moduleConfig"),Ne.config||qe(!1,"Null moduleConfig.config"),"function"==typeof e){const t=e(Ve.api);if(t.ready)throw new Error("Module.ready couldn't be redefined.");Object.assign(Be,t),Te(Be,t)}else{if("object"!=typeof e)throw new Error("Can't use moduleFactory callback of createDotnetRuntime function.");Te(Be,e)}await async function(e){if(De){const e=await import(/*! webpackIgnore: true */"process"),t=14;if(e.versions.node.split(".")[0]<t)throw new Error(`NodeJS at '${e.execPath}' has too low version '${e.versions.node}', please use at least ${t}. See also https://aka.ms/dotnet-wasm-features`)}const t=/*! webpackIgnore: true */import.meta.url,o=t.indexOf("?");var n;if(o>0&&(Ne.modulesUniqueQuery=t.substring(o)),Ne.scriptUrl=t.replace(/\\/g,"/").replace(/[?#].*/,""),Ne.scriptDirectory=(n=Ne.scriptUrl).slice(0,n.lastIndexOf("/"))+"/",Ne.locateFile=e=>"URL"in globalThis&&globalThis.URL!==P?new URL(e,Ne.scriptDirectory).toString():L(e)?e:Ne.scriptDirectory+e,Ne.fetch_like=C,Ne.out=console.log,Ne.err=console.error,Ne.onDownloadResourceProgress=e.onDownloadResourceProgress,Ce&&globalThis.navigator){const e=globalThis.navigator,t=e.userAgentData&&e.userAgentData.brands;t&&t.length>0?Ne.isChromium=t.some((e=>"Google Chrome"===e.brand||"Microsoft Edge"===e.brand||"Chromium"===e.brand)):e.userAgent&&(Ne.isChromium=e.userAgent.includes("Chrome"),Ne.isFirefox=e.userAgent.includes("Firefox"))}ze.require=De?await import(/*! webpackIgnore: true */"module").then((e=>e.createRequire(/*! webpackIgnore: true */import.meta.url))):Promise.resolve((()=>{throw new Error("require not supported")})),void 0===globalThis.URL&&(globalThis.URL=P)}(Be)}}async function ut(e){return await dt(e),Ge=Be.onAbort,Ke=Be.onExit,Be.onAbort=Ye,Be.onExit=Xe,Be.ENVIRONMENT_IS_PTHREAD?async function(){(function(){const e=new MessageChannel,t=e.port1,o=e.port2;t.addEventListener("message",(e=>{var n,r;n=JSON.parse(e.data.config),r=JSON.parse(e.data.monoThreadInfo),lt?Ne.diagnosticTracing&&h("mono config already received"):(Ee(Ne.config,n),Ue.monoThreadInfo=r,je(),Ne.diagnosticTracing&&h("mono config received"),lt=!0,Ne.afterConfigLoaded.promise_control.resolve(Ne.config),Ce&&n.forwardConsoleLogsToWS&&void 0!==globalThis.WebSocket&&Ne.setup_proxy_console("worker-idle",console,globalThis.location.origin)),t.close(),o.close()}),{once:!0}),t.start(),self.postMessage({[l]:{monoCmd:"preload",port:o}},[o])})(),await Ne.afterConfigLoaded.promise,function(){const e=Ne.config;e.assets||qe(!1,"config.assets must be defined");for(const t of e.assets)ee(t),K[t.behavior]&&F.push(t)}(),setTimeout((async()=>{try{await re()}catch(e){et(1,e)}}),0);const e=ft(),t=await Promise.all(e);return await mt(t),Be}():async function(){var e;await Se(Be),se();const t=ft();(async function(){try{const e=oe("dotnetwasm");await le(e),e&&e.pendingDownloadInternal&&e.pendingDownloadInternal.response||qe(!1,"Can't load dotnet.native.wasm");const t=await e.pendingDownloadInternal.response,o=t.headers&&t.headers.get?t.headers.get("Content-Type"):void 0;let n;if("function"==typeof WebAssembly.compileStreaming&&"application/wasm"===o)n=await WebAssembly.compileStreaming(t);else{Ce&&"application/wasm"!==o&&y('WebAssembly resource does not have the expected content type "application/wasm", so falling back to slower ArrayBuffer instantiation.');const e=await t.arrayBuffer();Ne.diagnosticTracing&&h("instantiate_wasm_module buffered"),n=Me?await Promise.resolve(new WebAssembly.Module(e)):await WebAssembly.compile(e)}e.pendingDownloadInternal=null,e.pendingDownload=null,e.buffer=null,e.moduleExports=null,Ne.wasmCompilePromise.promise_control.resolve(n)}catch(e){Ne.wasmCompilePromise.promise_control.reject(e)}})(),setTimeout((async()=>{try{k(),function(){const e=oe("js-module-threads"),t=[];for(let o=0;o<Ne.config.pthreadPoolInitialSize;o++){const o=Ne.workerNextNumber++,n=new Worker(e.resolvedUrl,{name:"dotnet-worker-"+o.toString().padStart(3,"0"),type:"module"});n.info={workerNumber:o,pthreadId:0,reuseCount:0,updateCount:0,threadPrefix:"          -    ",threadName:"emscripten-pool"},t.push(n)}Ne.loadingWorkers.promise_control.resolve(t)}(),await re()}catch(e){et(1,e)}}),0);const o=await Promise.all(t);return await mt(o),await Ue.dotnetReady.promise,await ye(null===(e=Ne.config.resources)||void 0===e?void 0:e.modulesAfterRuntimeReady),await ve("onRuntimeReady",[Ve.api]),$e}()}function ft(){const e=oe("js-module-runtime"),t=oe("js-module-native");if(it&&st)return[it,st,at];"object"==typeof e.moduleExports?it=e.moduleExports:(Ne.diagnosticTracing&&h(`Attempting to import '${e.resolvedUrl}' for ${e.name}`),it=import(/*! webpackIgnore: true */e.resolvedUrl)),"object"==typeof t.moduleExports?st=t.moduleExports:(Ne.diagnosticTracing&&h(`Attempting to import '${t.resolvedUrl}' for ${t.name}`),st=import(/*! webpackIgnore: true */t.resolvedUrl));const o=te("js-module-diagnostics");return o&&("object"==typeof o.moduleExports?at=o.moduleExports:(Ne.diagnosticTracing&&h(`Attempting to import '${o.resolvedUrl}' for ${o.name}`),at=import(/*! webpackIgnore: true */o.resolvedUrl))),[it,st,at]}async function mt(e){const{initializeExports:t,initializeReplacements:o,configureRuntimeStartup:n,configureEmscriptenStartup:r,configureWorkerStartup:i,setRuntimeGlobals:s,passEmscriptenInternals:a}=e[0],{default:l}=e[1],c=e[2];s(Ve),t(Ve),c&&c.setRuntimeGlobals(Ve),await n(Be),Ne.runtimeModuleLoaded.promise_control.resolve(),l((e=>(Object.assign(Be,{ready:e.ready,__dotnet_runtime:{initializeReplacements:o,configureEmscriptenStartup:r,configureWorkerStartup:i,passEmscriptenInternals:a}}),Be))).catch((e=>{if(e.message&&e.message.toLowerCase().includes("out of memory"))throw new Error(".NET runtime has failed to start, because too much memory was requested. Please decrease the memory by adjusting EmccMaximumHeapSize. See also https://aka.ms/dotnet-wasm-features");throw e}))}const gt=new class{withModuleConfig(e){try{return Te(Be,e),this}catch(e){throw et(1,e),e}}withOnConfigLoaded(e){try{return Te(Be,{onConfigLoaded:e}),this}catch(e){throw et(1,e),e}}withConsoleForwarding(){try{return Ee(Fe,{forwardConsoleLogsToWS:!0}),this}catch(e){throw et(1,e),e}}withExitOnUnhandledError(){try{return Ee(Fe,{exitOnUnhandledError:!0}),Qe(),this}catch(e){throw et(1,e),e}}withAsyncFlushOnExit(){try{return Ee(Fe,{asyncFlushOnExit:!0}),this}catch(e){throw et(1,e),e}}withExitCodeLogging(){try{return Ee(Fe,{logExitCode:!0}),this}catch(e){throw et(1,e),e}}withElementOnExit(){try{return Ee(Fe,{appendElementOnExit:!0}),this}catch(e){throw et(1,e),e}}withInteropCleanupOnExit(){try{return Ee(Fe,{interopCleanupOnExit:!0}),this}catch(e){throw et(1,e),e}}withDumpThreadsOnNonZeroExit(){try{return Ee(Fe,{dumpThreadsOnNonZeroExit:!0}),this}catch(e){throw et(1,e),e}}withWaitingForDebugger(e){try{return Ee(Fe,{waitForDebugger:e}),this}catch(e){throw et(1,e),e}}withInterpreterPgo(e,t){try{return Ee(Fe,{interpreterPgo:e,interpreterPgoSaveDelay:t}),Fe.runtimeOptions?Fe.runtimeOptions.push("--interp-pgo-recording"):Fe.runtimeOptions=["--interp-pgo-recording"],this}catch(e){throw et(1,e),e}}withConfig(e){try{return Ee(Fe,e),this}catch(e){throw et(1,e),e}}withConfigSrc(e){try{return e&&"string"==typeof e||qe(!1,"must be file path or URL"),Te(Be,{configSrc:e}),this}catch(e){throw et(1,e),e}}withVirtualWorkingDirectory(e){try{return e&&"string"==typeof e||qe(!1,"must be directory path"),Ee(Fe,{virtualWorkingDirectory:e}),this}catch(e){throw et(1,e),e}}withEnvironmentVariable(e,t){try{const o={};return o[e]=t,Ee(Fe,{environmentVariables:o}),this}catch(e){throw et(1,e),e}}withEnvironmentVariables(e){try{return e&&"object"==typeof e||qe(!1,"must be dictionary object"),Ee(Fe,{environmentVariables:e}),this}catch(e){throw et(1,e),e}}withDiagnosticTracing(e){try{return"boolean"!=typeof e&&qe(!1,"must be boolean"),Ee(Fe,{diagnosticTracing:e}),this}catch(e){throw et(1,e),e}}withDebugging(e){try{return null!=e&&"number"==typeof e||qe(!1,"must be number"),Ee(Fe,{debugLevel:e}),this}catch(e){throw et(1,e),e}}withApplicationArguments(...e){try{return e&&Array.isArray(e)||qe(!1,"must be array of strings"),Ee(Fe,{applicationArguments:e}),this}catch(e){throw et(1,e),e}}withRuntimeOptions(e){try{return e&&Array.isArray(e)||qe(!1,"must be array of strings"),Fe.runtimeOptions?Fe.runtimeOptions.push(...e):Fe.runtimeOptions=e,this}catch(e){throw et(1,e),e}}withMainAssembly(e){try{return Ee(Fe,{mainAssemblyName:e}),this}catch(e){throw et(1,e),e}}withApplicationArgumentsFromQuery(){try{if(!globalThis.window)throw new Error("Missing window to the query parameters from");if(void 0===globalThis.URLSearchParams)throw new Error("URLSearchParams is supported");const e=new URLSearchParams(globalThis.window.location.search).getAll("arg");return this.withApplicationArguments(...e)}catch(e){throw et(1,e),e}}withApplicationEnvironment(e){try{return Ee(Fe,{applicationEnvironment:e}),this}catch(e){throw et(1,e),e}}withApplicationCulture(e){try{return Ee(Fe,{applicationCulture:e}),this}catch(e){throw et(1,e),e}}withResourceLoader(e){try{return Ne.loadBootResource=e,this}catch(e){throw et(1,e),e}}async download(){try{await async function(){dt(Be),await Se(Be),se(),k(),re(),await Ne.allDownloadsFinished.promise}()}catch(e){throw et(1,e),e}}async create(){try{return this.instance||(this.instance=await async function(){return await ut(Be),Ve.api}()),this.instance}catch(e){throw et(1,e),e}}async run(){try{return Be.config||qe(!1,"Null moduleConfig.config"),this.instance||await this.create(),this.instance.runMainAndExit()}catch(e){throw et(1,e),e}}},pt=et,ht=ut;Me||"function"==typeof globalThis.URL||qe(!1,"This browser/engine doesn't support URL API. Please use a modern version. See also https://aka.ms/dotnet-wasm-features"),"function"!=typeof globalThis.BigInt64Array&&qe(!1,"This browser/engine doesn't support BigInt64Array API. Please use a modern version. See also https://aka.ms/dotnet-wasm-features"),(Me||De)&&qe(!1,"This build of dotnet is multi-threaded, it doesn't support shell environments like V8 or NodeJS. See also https://aka.ms/dotnet-wasm-features"),void 0===globalThis.SharedArrayBuffer&&qe(!1,"SharedArrayBuffer is not enabled on this page. Please use a modern browser and set Cross-Origin-Opener-Policy and Cross-Origin-Embedder-Policy http headers. See also https://aka.ms/dotnet-wasm-features"),"function"!=typeof globalThis.EventTarget&&qe(!1,"This browser/engine doesn't support EventTarget API. Please use a modern version. See also https://aka.ms/dotnet-wasm-features"),gt.withConfig(/*json-start*/{
  "mainAssemblyName": "Akbura.Playground.Browser",
  "resources": {
    "hash": "sha256-J3JlUPbdvgC8O4rqFQOJlFmr8Nmag1ewXEA+A/2CrR4=",
    "jsModuleWorker": [
      {
        "name": "dotnet.native.worker.8duaz980cg.mjs"
      }
    ],
    "jsModuleNative": [
      {
        "name": "dotnet.native.gj0xrgw4pv.js"
      }
    ],
    "jsModuleRuntime": [
      {
        "name": "dotnet.runtime.njhzbyt8kw.js"
      }
    ],
    "wasmNative": [
      {
        "name": "dotnet.native.rrfjufru1o.wasm",
        "hash": "sha256-l4BBdkzpWsp6tbhBi90+TV9eTV8E0Im1lXSTd87pg84=",
        "cache": "force-cache"
      }
    ],
    "icu": [
      {
        "virtualPath": "icudt_CJK.dat",
        "name": "icudt_CJK.tjcz0u77k5.dat",
        "hash": "sha256-SZLtQnRc0JkwqHab0VUVP7T3uBPSeYzxzDnpxPpUnHk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "icudt_EFIGS.dat",
        "name": "icudt_EFIGS.tptq2av103.dat",
        "hash": "sha256-8fItetYY8kQ0ww6oxwTLiT3oXlBwHKumbeP2pRF4yTc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "icudt_no_CJK.dat",
        "name": "icudt_no_CJK.lfu7j35m59.dat",
        "hash": "sha256-L7sV7NEYP37/Qr2FPCePo5cJqRgTXRwGHuwF5Q+0Nfs=",
        "cache": "force-cache"
      }
    ],
    "coreAssembly": [
      {
        "virtualPath": "System.Collections.Concurrent.dll",
        "name": "System.Collections.Concurrent.0jzg9r0pnc.dll",
        "hash": "sha256-3Ext4oDXgD8UAyKGQQMsEzp0NaZXygMl1zq28qu0nmM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.dll",
        "name": "System.Collections.btpkisew0y.dll",
        "hash": "sha256-UR739acxNI/1FOsmGrrzsYYHCjEt1MySTwwRgefCf4M=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.CoreLib.dll",
        "name": "System.Private.CoreLib.xupcwec8lz.dll",
        "hash": "sha256-6eU5Iq9G6/skUnEWmIxIzQArQHKKHr4fWMc74mdo8wg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.InteropServices.JavaScript.dll",
        "name": "System.Runtime.InteropServices.JavaScript.uvnab833sn.dll",
        "hash": "sha256-lm3pFj7evL34jCYjwn9XDwRmtFqQh1yfk8jxnV7WB1U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Channels.dll",
        "name": "System.Threading.Channels.fzucdz9n89.dll",
        "hash": "sha256-2xDWVDiE3EUBkP3sFgsua7uaSG2B0BGNfMxY+RVUl94=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.ThreadPool.dll",
        "name": "System.Threading.ThreadPool.8g626if5kq.dll",
        "hash": "sha256-KAtADDeHfCl+YuBs2ZgJ5yust1ak4yxDw0HY8mtzCio=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.dll",
        "name": "System.Threading.x3vv0fhomy.dll",
        "hash": "sha256-PVt6j/e4/o8kqi9VqHHeZYlsxQJ20BFefLURM0Vx6MY=",
        "cache": "force-cache"
      }
    ],
    "assembly": [
      {
        "virtualPath": "Akbura.AvaloniaEdit.dll",
        "name": "Akbura.AvaloniaEdit.y5du0q9xlc.dll",
        "hash": "sha256-JhYdYUDrU1ZSvN+RnSWA1eE8rAduB0PE4lOgqEJtrjw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Akbura.Generator.dll",
        "name": "Akbura.Generator.e8i4wkoeu2.dll",
        "hash": "sha256-stqc6gLzxQrtWHqEXkBoRJZA6Is5d+H1cYd4X/tiScc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Akbura.Playground.Browser.dll",
        "name": "Akbura.Playground.Browser.hd8jicixo3.dll",
        "hash": "sha256-2+wbkip7yAKuhAeJkYulZ7SW/H4jc4lQi3BgjrnVWS0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Akbura.Playground.dll",
        "name": "Akbura.Playground.baf1znyhrc.dll",
        "hash": "sha256-oyK0Sizy1DkWJzUzyceR6buggdU40PrcWjNShcvMqTc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Akbura.Workspaces.dll",
        "name": "Akbura.Workspaces.t78a1a1nx0.dll",
        "hash": "sha256-595Mt7oQlFPwfG0KUCGgzsGvpxjvK5G+IdmRQjSs9lg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Akbura.dll",
        "name": "Akbura.ubh6s1e2wt.dll",
        "hash": "sha256-xsJ/baWz2I+2dG7k8P4Tf8t9znHLf7NTHmV+PIpQoAA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Avalonia.dll",
        "name": "Avalonia.9c4p22l87a.dll",
        "hash": "sha256-Obz3pcg0yTQFMfZu6Ij8VcxGWmw+BBq3qzhFDmucdMg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Avalonia.Base.dll",
        "name": "Avalonia.Base.vcbm5aqcfg.dll",
        "hash": "sha256-/w+8qY5CM8hhULG3xkk0IVatWciKKzDvUObkks/SJsA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Avalonia.Browser.dll",
        "name": "Avalonia.Browser.tjsj1ipfxs.dll",
        "hash": "sha256-SZBGXrB+YWIhn9dTw0jG3ubrrKtuKu+7/84cNtv/Bn8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Avalonia.Controls.dll",
        "name": "Avalonia.Controls.8nt4q1i0c6.dll",
        "hash": "sha256-7HJE078/lWTlbVhSNEvcmaldH5kue5Wr11CKJrb9QHo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Avalonia.DesignerSupport.dll",
        "name": "Avalonia.DesignerSupport.oc9ff57mzc.dll",
        "hash": "sha256-6I85r/b8VTDFUB/0gnEQy3mR+8rroZFeNOCwON4zJ24=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Avalonia.Dialogs.dll",
        "name": "Avalonia.Dialogs.jilbffciyj.dll",
        "hash": "sha256-VXNE2xcPipewur4KGx4++hBM7TeSxfmxbO7SfiOdO3Q=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Avalonia.Fonts.Inter.dll",
        "name": "Avalonia.Fonts.Inter.nozy4lvcnv.dll",
        "hash": "sha256-J9Eva2s4qX9uugrFEX4eiY/1+7H+SKcq9ou71BoBNus=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Avalonia.HarfBuzz.dll",
        "name": "Avalonia.HarfBuzz.vo289xmdm7.dll",
        "hash": "sha256-nyyivMrMDMFtILwZCSbYOpuM4md6BR8UJkFgQNKKKKE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Avalonia.Markup.dll",
        "name": "Avalonia.Markup.8intk5d2v0.dll",
        "hash": "sha256-wMsV0Ne/yaiL0aTIVYu+xl+oiyW0obbEYk1ZltY+EdY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Avalonia.Markup.Xaml.dll",
        "name": "Avalonia.Markup.Xaml.6z9lpisehq.dll",
        "hash": "sha256-wqY1rALrdG4UMFcYME0ujZemIk1vMkq7zrJog7eZ1QA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Avalonia.Metal.dll",
        "name": "Avalonia.Metal.tbklh57q6n.dll",
        "hash": "sha256-KS8WRNHjDbwqo9VDB9MFA0XbFwFxcGr9nFf/JU303z4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Avalonia.MicroCom.dll",
        "name": "Avalonia.MicroCom.o1qxvn3ews.dll",
        "hash": "sha256-5BK0UIN4VdCIRVW7Zrm7Dk4UWV+hkoG12v1ZuY76Eoo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Avalonia.OpenGL.dll",
        "name": "Avalonia.OpenGL.h6yfs7yi7p.dll",
        "hash": "sha256-yRL/e34R2aJ0j8wm3Gwv88p8dsJesHfHYZc4Qj2Akv8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Avalonia.Remote.Protocol.dll",
        "name": "Avalonia.Remote.Protocol.s1yi0tx94g.dll",
        "hash": "sha256-IJ942OkCMiq48RDWtOLYwrp3zD33xb0PIj6hfD3ppZw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Avalonia.Skia.dll",
        "name": "Avalonia.Skia.4qak1r0bpu.dll",
        "hash": "sha256-TCDlnTZxzj98TmxAggvX8IcIAsa4MQm2uz+5TEyKl5I=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Avalonia.Themes.Fluent.dll",
        "name": "Avalonia.Themes.Fluent.y1y4hnwy9n.dll",
        "hash": "sha256-ZnLXDCIzbmwjjGAGX114xgGLB3m98C3V3M+QvaRssGA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Avalonia.Vulkan.dll",
        "name": "Avalonia.Vulkan.dmm7hiqi6q.dll",
        "hash": "sha256-Rcf+TWBiF0gc4Folhn61+yte/MHeY/pYm4tHM5u9Zyo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "AvaloniaEdit.dll",
        "name": "AvaloniaEdit.ccj1mteqwk.dll",
        "hash": "sha256-LOJOHFNe3YUdBmUf0OqFFc9J6ZTLomRQL6UNuJd5zq0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "HarfBuzzSharp.dll",
        "name": "HarfBuzzSharp.1t4k6bwzpk.dll",
        "hash": "sha256-VecqwjJj+OEkhPNDXgLsIvrJ7/bm0Wm7Cowka+a5nSk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Humanizer.dll",
        "name": "Humanizer.umqku5jypw.dll",
        "hash": "sha256-1oUwdVR9fjA+/WA1TZEaL/GO26WCzaL6WdkaLl3Pnpg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "MicroCom.Runtime.dll",
        "name": "MicroCom.Runtime.i3zehw7n72.dll",
        "hash": "sha256-LgpDvj0SPa5aqKfxCC/T8cKo2EL2xRPerFJtrtYg4xg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.CSharp.dll",
        "name": "Microsoft.CSharp.m9xkaqowx6.dll",
        "hash": "sha256-lqhB3t3k+A+KJMl2c0uVo5jy8UvphTTIU7ghqs7KdCk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.CodeAnalysis.dll",
        "name": "Microsoft.CodeAnalysis.79eq0hneh6.dll",
        "hash": "sha256-VWGNRXEo7sWCAXVFuJKevxlWaQA7bsc3EQ+VGeFxHSg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.dll",
        "name": "Microsoft.CodeAnalysis.CSharp.Features.osy9vcb8sy.dll",
        "hash": "sha256-mAjTdUEQ0ebcwtxM2FwLmBSMpDhKSuz+zo/MuZT4eR0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.dll",
        "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.t58c6kz4qq.dll",
        "hash": "sha256-r4Ue248MEoy/oyvoFZY4HrLIBuSpYbULR4JVNvP7Ntk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.CodeAnalysis.CSharp.dll",
        "name": "Microsoft.CodeAnalysis.CSharp.gde5oa5ks3.dll",
        "hash": "sha256-/FcaZkesRbCw3Bhmt5VSm7LuuTrfm+l/zVkDz0gvcAo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.CodeAnalysis.Elfie.dll",
        "name": "Microsoft.CodeAnalysis.Elfie.62gdi7eyhm.dll",
        "hash": "sha256-jsDhisVwFXs4Y/cANbZEkl2JmWrZAfMe+RjSsPdCiUE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.CodeAnalysis.Features.dll",
        "name": "Microsoft.CodeAnalysis.Features.67cp7z4bld.dll",
        "hash": "sha256-/iJ0Sqd1b47qUJutnkuXqDtLd8LZvx5i76+XlZ/cA0c=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.CodeAnalysis.Scripting.dll",
        "name": "Microsoft.CodeAnalysis.Scripting.e9rqdohrjd.dll",
        "hash": "sha256-fpaowLuPY8r9VO4nTRxjjsYJg0IH/vNUacAaLk7wZdE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.CodeAnalysis.Workspaces.dll",
        "name": "Microsoft.CodeAnalysis.Workspaces.ci9jnua4d1.dll",
        "hash": "sha256-5AXL4IW7jxo/zHMIrw3GxbO+SVGWBGoYQGB7F7q+E6A=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.DiaSymReader.dll",
        "name": "Microsoft.DiaSymReader.zjf3t0xi2e.dll",
        "hash": "sha256-fz51QzIf9MJnW5TlakoyyTXBHIL8xg1PbLYRywqNqtw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Language.Xml.dll",
        "name": "Microsoft.Language.Xml.7o4ebbwgd8.dll",
        "hash": "sha256-2fmTUeb5qUnKpW1Iy4EvDmjzddQHdQBUuG+LRETkC3E=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.VisualBasic.Core.dll",
        "name": "Microsoft.VisualBasic.Core.prm1a054w9.dll",
        "hash": "sha256-hcMpsV/7nq0Oqp+tl33PWm7vvmsTeP5AHFrRUQJTGmY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.VisualBasic.dll",
        "name": "Microsoft.VisualBasic.dcuubeityw.dll",
        "hash": "sha256-48fcwoU5SWLF7zf0JDqtax3NF2PWtKimAhe2PoVzmPg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Win32.Primitives.dll",
        "name": "Microsoft.Win32.Primitives.6ut8jvxzci.dll",
        "hash": "sha256-Duj+YqfqoxEPbQIiMJq+E99maTJZhoyq53dIRVBAXc0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Win32.Registry.dll",
        "name": "Microsoft.Win32.Registry.c10824opo4.dll",
        "hash": "sha256-0LsGMqHoQXaN4rDkf1SwO0OPAJ4h8T02QMeMurbMgKQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "SkiaSharp.dll",
        "name": "SkiaSharp.yut6u0nqvs.dll",
        "hash": "sha256-qqqhjGi6Hzo0CLAN/yixHVcFGY4Xup06pZIiv9NUB8g=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.AppContext.dll",
        "name": "System.AppContext.7yxbwvztqi.dll",
        "hash": "sha256-n+w0iLbAeNAPUPr1ttlCLuOHIjAkfKSjI1XHwpoc6fk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Buffers.dll",
        "name": "System.Buffers.b3vzwvpucd.dll",
        "hash": "sha256-2Wsk9HvN6If7EwbztKXRlHGDLdQPbqt5q1xFA7Yh2Fc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.Immutable.dll",
        "name": "System.Collections.Immutable.uo85u1b6yb.dll",
        "hash": "sha256-Pm0YY5/HolUWpH/aWB7PQR6GRTtOTLzvnFwED5FPPUo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.NonGeneric.dll",
        "name": "System.Collections.NonGeneric.o88smaqtir.dll",
        "hash": "sha256-SMkKNPTO3kXapmKYbW04jfrqMwnryCAmQFBUlx2Lh00=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.Specialized.dll",
        "name": "System.Collections.Specialized.wcjs47n7fs.dll",
        "hash": "sha256-AH/dB29vorqUGCf3+3o8TSJKCaUX9um30s9OKuX/xFE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.Annotations.dll",
        "name": "System.ComponentModel.Annotations.6pni3rl6ev.dll",
        "hash": "sha256-ekfl7/t3HxETNIkDsRtkEN76F39HDN7s4hdbfN2eXmw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.DataAnnotations.dll",
        "name": "System.ComponentModel.DataAnnotations.xf1791ihpl.dll",
        "hash": "sha256-80/LbsQAXQbuLjLPpO4yvrB6HFwcuZQLkVlVfKYMfnc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.EventBasedAsync.dll",
        "name": "System.ComponentModel.EventBasedAsync.b2t4w92eu8.dll",
        "hash": "sha256-3Q+1OsOl62zk0X7b52AGbIRB6Btkc9S/qzz/N+mVNKc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.Primitives.dll",
        "name": "System.ComponentModel.Primitives.oocxploolf.dll",
        "hash": "sha256-iJRnpbVs6TGrQ/svQVO9qBKYIALx6cvG8/VU1EuSmtE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.TypeConverter.dll",
        "name": "System.ComponentModel.TypeConverter.2k5as3kw0w.dll",
        "hash": "sha256-ogJqWuPCkh02/k9OwofL0R+qNBxI0lgdpkTapZ+WQ6c=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.dll",
        "name": "System.ComponentModel.sbz9jrkn95.dll",
        "hash": "sha256-GFqvgvmLtUA4zBROUsP/njJ9bqzfZdyGPPqfu0M/u4E=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Composition.AttributedModel.dll",
        "name": "System.Composition.AttributedModel.9f5066fsyt.dll",
        "hash": "sha256-damA0zZDxNBTJG5M0qJtF85oktYJu/ma1tn8Q8+/eac=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Composition.Convention.dll",
        "name": "System.Composition.Convention.psm6v635j2.dll",
        "hash": "sha256-6X8ua5n5gB11IEJBm1WrLaf6pvM1DcuR0DD37eBCXK4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Composition.Hosting.dll",
        "name": "System.Composition.Hosting.192nofh8zg.dll",
        "hash": "sha256-29Cjyf6Cbl+ofGFqo0WlY8xeeaVjGTyvQ1Z2egl+I0M=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Composition.Runtime.dll",
        "name": "System.Composition.Runtime.329vd2fexj.dll",
        "hash": "sha256-ZdsQ4DQICYmEkWzioATTjycqbGd/b65bKkOXU39QQLg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Composition.TypedParts.dll",
        "name": "System.Composition.TypedParts.kdxzbeo3bb.dll",
        "hash": "sha256-SEl8PRw3sLnpgXkQVnnOFD1CUzKqydBw10eHZ19KiHA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Configuration.ConfigurationManager.dll",
        "name": "System.Configuration.ConfigurationManager.wfwyzqheae.dll",
        "hash": "sha256-vBPYmvTnZmhPpkpfa/EGsTJqG9Jm+bgsu/fN1brgebQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Configuration.dll",
        "name": "System.Configuration.kavjmdj4vc.dll",
        "hash": "sha256-LH9CilvOW21IfwfR/EtBxkSjs8xfLTb9F1JaopQ+vmk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Console.dll",
        "name": "System.Console.3xmtffinvq.dll",
        "hash": "sha256-ufsshvbmSdWbw7azX02h4U7CtjSOxqOhzE4MKio18aw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Core.dll",
        "name": "System.Core.1ggm5qtcco.dll",
        "hash": "sha256-wZnAfU4PSyQBlMldxLFU7bMvt/QJvfhX4j4b61Behrc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Data.dll",
        "name": "System.Data.19fh5fwxaj.dll",
        "hash": "sha256-i5cF4nu6dnPC3JckcO5Zp37nh2SkFC9SULR25C1txJs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Data.Common.dll",
        "name": "System.Data.Common.zik4nztbni.dll",
        "hash": "sha256-67NVG3oKpiwEDFga8eGbiyYa+ad1+R1I4pFw5VeyrAE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Data.DataSetExtensions.dll",
        "name": "System.Data.DataSetExtensions.3hiqk56uiu.dll",
        "hash": "sha256-+XfnZhIw9yXPuApzV96THK85FETCNor8AvO5aW/UmRI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.Contracts.dll",
        "name": "System.Diagnostics.Contracts.vp1y3z1icp.dll",
        "hash": "sha256-Mxx5Xcig2fAKW3Qj7ceq1ZBHAw+QwnjjNWTPMdfx+3s=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.Debug.dll",
        "name": "System.Diagnostics.Debug.hh8wful1fm.dll",
        "hash": "sha256-C9b3fcvnT/HTOsDhUaJAIG3xlDJECQszmZEXKZtlS5E=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.DiagnosticSource.dll",
        "name": "System.Diagnostics.DiagnosticSource.hzwhp5hu7k.dll",
        "hash": "sha256-PT7l3chjCOYFgenmxq3bRs6oGKeGiAE4qBXQyWXe83M=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.EventLog.dll",
        "name": "System.Diagnostics.EventLog.n5t6z5njay.dll",
        "hash": "sha256-G0BW8WwOuIZK5rHZ1zkMzYl3ROmCXYnFcKwYaO+7aKw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.FileVersionInfo.dll",
        "name": "System.Diagnostics.FileVersionInfo.rfsk33wm75.dll",
        "hash": "sha256-9xvCOZe1Uvoja6KbvU7JoUp40FGQZO2NGu9SCKVaJ+I=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.Process.dll",
        "name": "System.Diagnostics.Process.d82tdwkn75.dll",
        "hash": "sha256-c6Ip90ue3GzLPmf04PPJLGImxcEXmoF+oIlabInRbA8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.StackTrace.dll",
        "name": "System.Diagnostics.StackTrace.qvfcqu3w09.dll",
        "hash": "sha256-mmIE2WurwuuaU3HabJW/L4hdyQz94xaMWfaSc2HqsuA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.TextWriterTraceListener.dll",
        "name": "System.Diagnostics.TextWriterTraceListener.30x7ja79d0.dll",
        "hash": "sha256-LVISdcBpmbHwdceH+pU7ay9D4lqFyDCZfGeZfV0Bf2s=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.Tools.dll",
        "name": "System.Diagnostics.Tools.ytrqoi0xw2.dll",
        "hash": "sha256-Zu+XjzLJHjwnK2K96+O373M67qkQUW/yUBzxrz12Ud8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.TraceSource.dll",
        "name": "System.Diagnostics.TraceSource.chg9p536b6.dll",
        "hash": "sha256-sFiSqBSrn5tIN0Lg2Etooy07kWtQg/ONsjTEaJTe+0w=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.Tracing.dll",
        "name": "System.Diagnostics.Tracing.11l11ensm6.dll",
        "hash": "sha256-tSnnWYND088iw/dv9TFkNuRIH8faib32+ctmNKfqRME=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Drawing.Primitives.dll",
        "name": "System.Drawing.Primitives.lxfjv8v1vi.dll",
        "hash": "sha256-aaYFSXR71NlgMiOvsQyxtPozkkrSWfqY0WyzJZsZGPE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Drawing.dll",
        "name": "System.Drawing.llbpsodvcy.dll",
        "hash": "sha256-B22fkwz2jWjqnfni1ECmSs/sW3F83T23fNfw5PXYgpE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Dynamic.Runtime.dll",
        "name": "System.Dynamic.Runtime.b5dlbzir0w.dll",
        "hash": "sha256-MZOyDzkp0zGS39vg/9cxBNhm3YdOCtJCVHzs1LGW52Q=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Formats.Asn1.dll",
        "name": "System.Formats.Asn1.3ydnpoed0h.dll",
        "hash": "sha256-W9+QI4/z3V1/r/X0EdeRSX5e5Yt/V9h+EyXxtirV6BQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Formats.Tar.dll",
        "name": "System.Formats.Tar.qmvw8rp7bk.dll",
        "hash": "sha256-IgzbjWixxVUIEXMBs87YIBHERcmFr2gvCwHoVLrwgYA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Globalization.Calendars.dll",
        "name": "System.Globalization.Calendars.ynbbekrb97.dll",
        "hash": "sha256-MuJzVKttV/v+Kah+kZvgdKYJJBGk8WGQiCUOh2D14mk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Globalization.Extensions.dll",
        "name": "System.Globalization.Extensions.1a5eu02arv.dll",
        "hash": "sha256-OZB+WPI0pqIdhG1MuYQmGGtnLOEzhTos5W8ONE06fCI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Globalization.dll",
        "name": "System.Globalization.fjds2dwxzq.dll",
        "hash": "sha256-dfa6242Q/kHtl9Iiv8Voxf5FLXMWd6j8lmW3P0M+jTM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.dll",
        "name": "System.IO.77dfxjh7kf.dll",
        "hash": "sha256-bVI5j8p74svQWE0vnDuVAvjSDhjroAYtOAQjdxn+k1Q=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Compression.Brotli.dll",
        "name": "System.IO.Compression.Brotli.vuscninxlq.dll",
        "hash": "sha256-F8zzzcF8OlYdMGQ4fYp/zr9su5+tczs4+ngkrwxX//A=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Compression.FileSystem.dll",
        "name": "System.IO.Compression.FileSystem.n2lvuny1v1.dll",
        "hash": "sha256-kYlStf9uFazq9ZmzM/DSpHmvbuwD3WsaUd9ONTJu0eA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Compression.ZipFile.dll",
        "name": "System.IO.Compression.ZipFile.5i1vc45ub0.dll",
        "hash": "sha256-XQUmXPHOb/1nZ/pM9rD/n+apvGWvS/uAqSzQL+CC6Yw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Compression.dll",
        "name": "System.IO.Compression.lllzyfm942.dll",
        "hash": "sha256-5wlI+XCrFF/pmq7v6ZN/mY3Jlc2Ie9n++okOUwygEMA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.FileSystem.AccessControl.dll",
        "name": "System.IO.FileSystem.AccessControl.rewnyxgsx6.dll",
        "hash": "sha256-04kwpSgB0CUXRVHIqtULzyrNLJTgOUz2RoOj9Ck1tu4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.FileSystem.DriveInfo.dll",
        "name": "System.IO.FileSystem.DriveInfo.rguvcpk4jb.dll",
        "hash": "sha256-+3upjpKVMSUL8c7O/LOfisnOfOsqMH+jauUEozRl2iw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.FileSystem.Primitives.dll",
        "name": "System.IO.FileSystem.Primitives.oz231c48i7.dll",
        "hash": "sha256-5MI3rHcgX+ADIlqiOAOghYrv4JbJkgLRzDAtzE0wX8o=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.FileSystem.Watcher.dll",
        "name": "System.IO.FileSystem.Watcher.zwwpj65wyn.dll",
        "hash": "sha256-44061nWGukJ6ICKph6kRdMhnEk+1EGr+v2XXbCUZlZY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.FileSystem.dll",
        "name": "System.IO.FileSystem.x3pinrjs5o.dll",
        "hash": "sha256-nUKcKFosVFM8FnEWujTbkreoOdKBUsjc3kZ3nS2xLD4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.IsolatedStorage.dll",
        "name": "System.IO.IsolatedStorage.qq4l4drwvv.dll",
        "hash": "sha256-vmzmRa1ADJK6IXNJQyA2OpyYP1YH1Z1x4/510qwZSJ8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.MemoryMappedFiles.dll",
        "name": "System.IO.MemoryMappedFiles.1l5i23y6ls.dll",
        "hash": "sha256-O0X16j0uuKm+bCXDyYaqN3++mO1gofGOnVxsgU4wxtI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Pipelines.dll",
        "name": "System.IO.Pipelines.31hpeppr0l.dll",
        "hash": "sha256-ibvPD5T/J138z7KS25PPEyNPOLqE8YmfW+fgkRAbe9Q=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Pipes.dll",
        "name": "System.IO.Pipes.13s8m3ab4k.dll",
        "hash": "sha256-053eKGkLDB2Pizwdb32MWPVfsbR6f3599C0aTVRk4E4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Pipes.AccessControl.dll",
        "name": "System.IO.Pipes.AccessControl.dl1tygn2lw.dll",
        "hash": "sha256-UT9SjomzqsYg6IN2pK/GCIyJgDYTzDXbSG9AqVk6HY8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.UnmanagedMemoryStream.dll",
        "name": "System.IO.UnmanagedMemoryStream.t2qklfk3zi.dll",
        "hash": "sha256-BZI7ledNVzklhwjrhzevbIYEVFsPcKkqzxvVyHBS3ic=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.dll",
        "name": "System.Linq.4imi96vsc0.dll",
        "hash": "sha256-bLMBnBidu98EPP8GxiHPptGLpeTp4gaBvITAJGRwEDI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.AsyncEnumerable.dll",
        "name": "System.Linq.AsyncEnumerable.62cqoe163t.dll",
        "hash": "sha256-cqAVvxvRloHIHkwMiYeChW7hiB5JOCKr6jqN61XfBGE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.Expressions.dll",
        "name": "System.Linq.Expressions.yjh6if7bhn.dll",
        "hash": "sha256-Xry059S2oi1dvlcA7qHs04CN+wmdZ1J07u6KLpSym9E=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.Parallel.dll",
        "name": "System.Linq.Parallel.7gqot9lgp9.dll",
        "hash": "sha256-GRPK/yYcXejIwqhjk+ihwZQ4qyOZEqzg2IHPbi0Qx9Y=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.Queryable.dll",
        "name": "System.Linq.Queryable.3fnnlwzofd.dll",
        "hash": "sha256-T7vnOh32uPF3DAgCokLeq7sHt+PectV+y/2T9SfJJio=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Memory.dll",
        "name": "System.Memory.8g8vwzfkos.dll",
        "hash": "sha256-iLiFq657MeZflHBnrKSIOz7xOBfMU75byzBiKC+Gg0M=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Http.dll",
        "name": "System.Net.Http.5q0vcjx7rk.dll",
        "hash": "sha256-bZ2jeq0SSF5y5uEqFdzdxIWS+j5R2TI6Mi1GE4E8ShQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Http.Json.dll",
        "name": "System.Net.Http.Json.29ldpd8ruc.dll",
        "hash": "sha256-KgaUVJDSOY/mcizaF02Cgf38B1CE/N77oIZ6rmz5ZLY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.HttpListener.dll",
        "name": "System.Net.HttpListener.62iqplhw9w.dll",
        "hash": "sha256-EuXLeG7+PlanlfYKDLj8bnr3F5bCklCXGiD/Tqs7NMg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Mail.dll",
        "name": "System.Net.Mail.hqxlmgteez.dll",
        "hash": "sha256-9/tLzM3C7lOwMPpNVexelTJjW9gwhET6Ux2HH2volaw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.NameResolution.dll",
        "name": "System.Net.NameResolution.avergw7qo8.dll",
        "hash": "sha256-BgSWuLXZ1QUQIWVyER+vmulyWWQaLWQJr3LNG+Jcb4Q=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.NetworkInformation.dll",
        "name": "System.Net.NetworkInformation.3sggkgnmcn.dll",
        "hash": "sha256-DYOdK224e7/3yOER5y/hkGQfnTxSETS9Ug/Ozh4gjAo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Ping.dll",
        "name": "System.Net.Ping.8dz320sm4q.dll",
        "hash": "sha256-zFcRWhklKUIUHKMhNOTyQFHcXj3tBKpaTiY6FgZh4cg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Primitives.dll",
        "name": "System.Net.Primitives.vky7k1r8g6.dll",
        "hash": "sha256-z5LRXacGWQkFM1RM1lvr30+KobDaYcUQx62dMAxmato=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Quic.dll",
        "name": "System.Net.Quic.kthegy3ewl.dll",
        "hash": "sha256-SCZZg2LkoeeaNUndkOd/EZ3bnA3ECoICzIS65gjKBiY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Requests.dll",
        "name": "System.Net.Requests.8hci0kxp8n.dll",
        "hash": "sha256-VNBU3ADOTbfj2HlaOBaHmO8r4vbIja2qLUNNILSrRQM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Security.dll",
        "name": "System.Net.Security.bukwuf4a2u.dll",
        "hash": "sha256-fZQD1wxxsGWKtbtOgsfJ3lgaioNnGvjoQKOJRL5hcfM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.ServerSentEvents.dll",
        "name": "System.Net.ServerSentEvents.fgeunms1qc.dll",
        "hash": "sha256-Ucy03v64ZEveWH6lQsFealD6GEc/X+CXJkehHqP8kPs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.ServicePoint.dll",
        "name": "System.Net.ServicePoint.okc4fimozf.dll",
        "hash": "sha256-WJC8y2Th0lTZr5KcaW9HQgnoN3fBChhXiHgVo55DkrE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Sockets.dll",
        "name": "System.Net.Sockets.4lncaufbrf.dll",
        "hash": "sha256-mMtRuOIoG8bIjRhl+adfnf6LpsD8IC2721l1SdV5GYI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebClient.dll",
        "name": "System.Net.WebClient.7czdhrzix2.dll",
        "hash": "sha256-2V7KBaCnbQ+HbZ/cKYc8yYjdJ5xnp9UGcUdiclhsJmU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebHeaderCollection.dll",
        "name": "System.Net.WebHeaderCollection.56qjouzhcl.dll",
        "hash": "sha256-PcuVtisWJGAUHSycWCubqk8gMbj9eJwKQ5unES5qtIU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebProxy.dll",
        "name": "System.Net.WebProxy.1rubnj9mrw.dll",
        "hash": "sha256-bSMMlbxEs3Rpb3Z5n/Y45iLjaeZDfBHMVsv2yG3T/XQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebSockets.Client.dll",
        "name": "System.Net.WebSockets.Client.mmq4klfaj0.dll",
        "hash": "sha256-ztc2gsh85L5hLgSQ8P35B9gQ9Y8PhKr8ZzLa1042Y6I=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebSockets.dll",
        "name": "System.Net.WebSockets.eqzj1xhu4e.dll",
        "hash": "sha256-Wm/h2czc3e69mRibVpQwyPUkPYkeoDRYQBupj6sn/S0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.dll",
        "name": "System.Net.q9sz0qsndi.dll",
        "hash": "sha256-Il5vlbOkilquzP9r5zuFY9dvaicgv7QIzLh0sprGwCI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Numerics.Vectors.dll",
        "name": "System.Numerics.Vectors.wogf3zykbf.dll",
        "hash": "sha256-QHHQOEDKYORyZ0bhsATxcUxlKgArl5f+5dcMQhNk3Fc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Numerics.dll",
        "name": "System.Numerics.wg4uhlu23o.dll",
        "hash": "sha256-4AjUbXKmMheSLztnmJ5st8Nc1Vlfcy1jkEvgujMnIxI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ObjectModel.dll",
        "name": "System.ObjectModel.qnbuq7hxiv.dll",
        "hash": "sha256-ekNhOELF9rPQTzQLC3skCt6SAZKGOI2DO+aDfjuGpG8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.DataContractSerialization.dll",
        "name": "System.Private.DataContractSerialization.y9eguwq905.dll",
        "hash": "sha256-Rup8s1NLntsCZu/RKH7b77QAUq0HbnI3CvwN2Kk1Lko=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.Uri.dll",
        "name": "System.Private.Uri.m0hwnv7mi4.dll",
        "hash": "sha256-2p48EhkTpdKi7LVmTi2/4Fyg6C75Y65guDfnUDM2zfM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.Xml.Linq.dll",
        "name": "System.Private.Xml.Linq.gbvccz42qj.dll",
        "hash": "sha256-dB5AdiArriCi2I/9TMjQYjY1IZcFLtp9QGKdzcZC+UQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.Xml.dll",
        "name": "System.Private.Xml.jg57sf9lr1.dll",
        "hash": "sha256-Yx+0evLTDdEKcS0pfQUBteeI4YKk4uULB9QUrDJXlg4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.dll",
        "name": "System.Reflection.7mfwj90xzz.dll",
        "hash": "sha256-8fHck648NZn3kd8B2spJKg2f489+ni2usiqmwVdukYg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.DispatchProxy.dll",
        "name": "System.Reflection.DispatchProxy.oa2wxj9em2.dll",
        "hash": "sha256-YC8U8iNAC/P4JKkGuxCQmj91Pa2EPJ0sCEIYr/atHdU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Emit.ILGeneration.dll",
        "name": "System.Reflection.Emit.ILGeneration.nn4lixoc96.dll",
        "hash": "sha256-Lf+e8caJRXfxoG5YKv9HiqYSdQEKcZi9UYIKTFp5qFY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Emit.Lightweight.dll",
        "name": "System.Reflection.Emit.Lightweight.x3wzxh3a80.dll",
        "hash": "sha256-sx1QFVB8QiH3lzetYLGb8jZSJtpxrZfc+tE7sFp5vu0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Emit.dll",
        "name": "System.Reflection.Emit.rt71xzbdjo.dll",
        "hash": "sha256-3/qkDAPmf5wdwwSjVNoWthXgQyq2Q2pPRnbi5xfYC8Y=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Extensions.dll",
        "name": "System.Reflection.Extensions.0sodv8qydh.dll",
        "hash": "sha256-sH1WUFJKB/E+OwoNVHgd4CarEqrqOyRXUxduCtXiyO4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Metadata.dll",
        "name": "System.Reflection.Metadata.2kwkipms87.dll",
        "hash": "sha256-0pekEcQNrwojuM56ezFzkEQWY5ecN26hUNh0niQb+wU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Primitives.dll",
        "name": "System.Reflection.Primitives.ef4xmn5uk8.dll",
        "hash": "sha256-qvbaZB5q/HgC7ZNLd+UbbihenYVCc/ZvVj2T/mqHn1g=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.TypeExtensions.dll",
        "name": "System.Reflection.TypeExtensions.yruvdhlpwn.dll",
        "hash": "sha256-crFqE4majm+oXvPTU/+lPGc0KKQaqAhElUcRgJWVCYE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Resources.Reader.dll",
        "name": "System.Resources.Reader.mgzn6cj0wk.dll",
        "hash": "sha256-Rrn4bTzQXU1he/aFNJilowGIFhVCB4YcVbwIRH6vRSw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Resources.ResourceManager.dll",
        "name": "System.Resources.ResourceManager.qwiutxodm1.dll",
        "hash": "sha256-OtwoWVgk/lklKD461OrX9qvzfDTf4f5SMue1RCvmr2E=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Resources.Writer.dll",
        "name": "System.Resources.Writer.ku03j9bz7i.dll",
        "hash": "sha256-DE7QuZaYrc1FwKVvd9Fj/L4ELSoahhdj+ymHiTezJmI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.dll",
        "name": "System.Runtime.5d8sviecq7.dll",
        "hash": "sha256-WQByL6hOmPEHA3BW3ndIdb7Ev108DF4u6go5n4ZYGYk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.CompilerServices.Unsafe.dll",
        "name": "System.Runtime.CompilerServices.Unsafe.xjgr5h6l9r.dll",
        "hash": "sha256-c8o8DY1XZ+TM2wpMwLZXA/EOQOn6iXPMqOG7Afpdhjs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.CompilerServices.VisualC.dll",
        "name": "System.Runtime.CompilerServices.VisualC.z2qktqhvy1.dll",
        "hash": "sha256-9YmFFfNGTkyFtrtr5CFbsJeVR8OTuUPjqCgZalynSfY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Extensions.dll",
        "name": "System.Runtime.Extensions.klh82ich2n.dll",
        "hash": "sha256-GNU9eArnJ/RZ4OnwEpjwy61bEFhaf3uTsQlrIrXoLMw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Handles.dll",
        "name": "System.Runtime.Handles.ufqbtbw4o3.dll",
        "hash": "sha256-mqMDhg1ZaSdlVnwdTA1KB366ywgPZDDciccAN51UULE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.InteropServices.RuntimeInformation.dll",
        "name": "System.Runtime.InteropServices.RuntimeInformation.j0mjsdzm0p.dll",
        "hash": "sha256-M5aCrBVNH+1lBKVDptDMY6SWX6a/bgm/E37+OsiiSLo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.InteropServices.dll",
        "name": "System.Runtime.InteropServices.tdcvykwqte.dll",
        "hash": "sha256-cQKMjwQuNZM+5FQt0WgwR93XudpSnqv7slzeq7XeHGQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Intrinsics.dll",
        "name": "System.Runtime.Intrinsics.aizvta1vse.dll",
        "hash": "sha256-fsXq8JFELXHpliq5zmAFbOc/hHGITtKoCMgxKUD1cHE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Loader.dll",
        "name": "System.Runtime.Loader.nwezk9zqtn.dll",
        "hash": "sha256-ycFrkMvqabDl6MqAd0auJqS+CylYmvZbyw2M9PjKynE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Numerics.dll",
        "name": "System.Runtime.Numerics.jyeauffee9.dll",
        "hash": "sha256-O1Rz6thvnUNH/bczJyUtuZ2Fl8c9giavAILz+c2G6p8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.dll",
        "name": "System.Runtime.Serialization.3d6zbdw2da.dll",
        "hash": "sha256-CTbPeLSS8V6BXiDcBqD4wq2f70MKNQSU/Siiot1GsOg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.Formatters.dll",
        "name": "System.Runtime.Serialization.Formatters.bge7si4uvf.dll",
        "hash": "sha256-6yjVhs0zKgRlZ0LxaCR0JrA5aSt/YJCLj6ianLw/UA4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.Json.dll",
        "name": "System.Runtime.Serialization.Json.t0ehdbwto3.dll",
        "hash": "sha256-PdqBNS9plpUF5neSsTiJDaSlFyjjrmwZctCyTCdZFFo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.Primitives.dll",
        "name": "System.Runtime.Serialization.Primitives.ghgrghsy0i.dll",
        "hash": "sha256-zD/b5/s3FnG6iwwYVYy/KBiS2HwDjdHMNqNsz25tKik=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.Xml.dll",
        "name": "System.Runtime.Serialization.Xml.wyrdq67r41.dll",
        "hash": "sha256-2DDWEIQbLcEmOGCQ06GTCbng88aLYHXXIObYGcVlloI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.AccessControl.dll",
        "name": "System.Security.AccessControl.f44nfbsrt8.dll",
        "hash": "sha256-YZCWrNXpXTXc5//VI3ITbvnJql60OY86e9y9jRM6WKQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Claims.dll",
        "name": "System.Security.Claims.2kio3aanxb.dll",
        "hash": "sha256-DuFrA5tToT6n981aHo3C4cojd0MBzs+Iv3W5G886hgs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.Algorithms.dll",
        "name": "System.Security.Cryptography.Algorithms.jggdg1l30j.dll",
        "hash": "sha256-bae+yAgHAjmmsExZgGy52OEWASYOKGtIUBhrfutz7xg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.Cng.dll",
        "name": "System.Security.Cryptography.Cng.gwneb2twoj.dll",
        "hash": "sha256-gHO6y2kf43IL/3cwyNVf766l2oSKsCL2E0vz89Lv5/Y=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.Csp.dll",
        "name": "System.Security.Cryptography.Csp.naos4flsvj.dll",
        "hash": "sha256-ASavz0msFLm1d5sCx5qChq9WNA+vL2RCyZbfegecDFM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.Encoding.dll",
        "name": "System.Security.Cryptography.Encoding.1qlhi4y758.dll",
        "hash": "sha256-OUpNCyD7T+sEwAhoH/OsukaRcTaH9MLhzNeYEiKqJOI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.OpenSsl.dll",
        "name": "System.Security.Cryptography.OpenSsl.g9lg616ncl.dll",
        "hash": "sha256-XKYdF7mnRHX+fWipuQ7O/TBsOESNEFVG6d83ynDYlq0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.Primitives.dll",
        "name": "System.Security.Cryptography.Primitives.uxy3wonhie.dll",
        "hash": "sha256-Xvyi3w0XyQGL5Qa2zM3A8q+UjZn1s4QFkrQhz/8KdEw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.ProtectedData.dll",
        "name": "System.Security.Cryptography.ProtectedData.oohodvqsj8.dll",
        "hash": "sha256-iBgSdn+g56McE5kZicrEe/vvykxvJwrJX095j8qg/U4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.X509Certificates.dll",
        "name": "System.Security.Cryptography.X509Certificates.qqd6g2er2y.dll",
        "hash": "sha256-EoNhvAUBj18Rfl881N1r4juRYaRH4iN2Aw8lH+Ufuuw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.dll",
        "name": "System.Security.Cryptography.ftzqmnibqp.dll",
        "hash": "sha256-LVYKQ3Z8UUOlQSFQMUr75hsEBp0pyKZ9zMm73Ni5D8U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Principal.Windows.dll",
        "name": "System.Security.Principal.Windows.8imwrpkfdw.dll",
        "hash": "sha256-EMdKNVDwIO/+eiJ8TAMgaQY9Nn2oTnay0+HCTVyqz2o=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Principal.dll",
        "name": "System.Security.Principal.w9ejeeib3o.dll",
        "hash": "sha256-BHX4a0WG3fQHJjiQqqvSYS75zx5v6jKPoebZ/Jly8VA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.SecureString.dll",
        "name": "System.Security.SecureString.x8oaw8989o.dll",
        "hash": "sha256-v5Zc4DpDf2v1V/AjpzLFO0HAemEvDegOOYQfAJRqQfE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.dll",
        "name": "System.Security.qio904b8a7.dll",
        "hash": "sha256-YiQ/90wkLFpxLZCGRxapcv/WyCXJvFlwPp4beDTZkss=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ServiceModel.Web.dll",
        "name": "System.ServiceModel.Web.01a4e113hi.dll",
        "hash": "sha256-PLIoDfmKeWrX14vUgcg22fzcZYwKAAkZpi3gdi9KKWU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ServiceProcess.dll",
        "name": "System.ServiceProcess.x0ui0jdu33.dll",
        "hash": "sha256-fxuakl+pXfOThxZ6zWmFYAuE9RdfuhmxhOVjtr2Typw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Encoding.CodePages.dll",
        "name": "System.Text.Encoding.CodePages.n6u7jfh0mm.dll",
        "hash": "sha256-ccy1Hgz0yLGqa9DN7qrB5A4hqKyXwEfNcM69pupLG2I=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Encoding.Extensions.dll",
        "name": "System.Text.Encoding.Extensions.jd4bze04by.dll",
        "hash": "sha256-GRqmhGne0AzTnZbCFx65hTEp1ieq8UJbVbxp59avLUo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Encoding.dll",
        "name": "System.Text.Encoding.ieg5rqp37b.dll",
        "hash": "sha256-StXCrC6kUysTvkDvDZWkhH9wItUBStXjdrNU3ldKq1A=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Encodings.Web.dll",
        "name": "System.Text.Encodings.Web.afvxpugxzb.dll",
        "hash": "sha256-Vk7eYKnqZw8wQDuJ3i3I9NofQOw8Sla34FV5OpRaGKU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Json.dll",
        "name": "System.Text.Json.gy8lkge8lf.dll",
        "hash": "sha256-mNQvq+pzrqEuZJ5gxk9HBosfE1xUfOTezY9s3IATM6E=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.RegularExpressions.dll",
        "name": "System.Text.RegularExpressions.hftkdiawhu.dll",
        "hash": "sha256-/SZjyo7g6qgHP48zY4H2SKIneiVG0Nx8GdEKQSdA108=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.AccessControl.dll",
        "name": "System.Threading.AccessControl.46pjrtdsqv.dll",
        "hash": "sha256-1Jpwttq932G2BtVLYtIzXty2WUuTOzmCyZ/d+kfi7mU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Overlapped.dll",
        "name": "System.Threading.Overlapped.vrwixw3qem.dll",
        "hash": "sha256-lZiaS2mHqr258MKoxzOPm5x0b7kXHR4xUGi8iYyEPWc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Tasks.dll",
        "name": "System.Threading.Tasks.1ksnc6xc3s.dll",
        "hash": "sha256-r5scbXn0IiiNG7ivD8zW3YZshNDT8bAIe2ulT8dm9ls=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Tasks.Dataflow.dll",
        "name": "System.Threading.Tasks.Dataflow.pkbhqr9787.dll",
        "hash": "sha256-2UoF6foh1P4Km6WvCYtRt7Gf8cfXW5FerbQkvoJv3Sk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Tasks.Extensions.dll",
        "name": "System.Threading.Tasks.Extensions.xkuk6q7vco.dll",
        "hash": "sha256-0XWsMeiae1gmDv4X/226hZiCM0eVLkypNEL43bqbiwE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Tasks.Parallel.dll",
        "name": "System.Threading.Tasks.Parallel.7snvuwmn2t.dll",
        "hash": "sha256-JwjF5agypcJeZ9iUIOXt+H69hhxfbkyat4yOWY/+R+U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Thread.dll",
        "name": "System.Threading.Thread.lupjypbk2w.dll",
        "hash": "sha256-Y+CLrsmG+TemJKCot+qE+jutWGCIB/6sFgxR6poUGAE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Timer.dll",
        "name": "System.Threading.Timer.8vw9ckdu25.dll",
        "hash": "sha256-pLqkhKw6rxIKg1ivUhe3qJ81aPeUTjaeaT9pHvVKHyU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Transactions.Local.dll",
        "name": "System.Transactions.Local.dtdgjn9got.dll",
        "hash": "sha256-8egu/e7aPBRkb65C0PQ2YolKxznpOQBeIo51RJT2eiE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Transactions.dll",
        "name": "System.Transactions.cv55qprw50.dll",
        "hash": "sha256-+LpbFsbvOBhMc12HA6MTOwLYrwDRbSjUPufaLzkDV70=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ValueTuple.dll",
        "name": "System.ValueTuple.lwm8x7n44k.dll",
        "hash": "sha256-9dL1DX/RHSYGZGkkTVAmf/Wji3nVxeSdutJjTaD+qXw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Web.HttpUtility.dll",
        "name": "System.Web.HttpUtility.j2jsaoon3h.dll",
        "hash": "sha256-izksx3bKjvpT0jUZ3OGVktCRJJSrIIxaWt9yR5C/+yE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Web.dll",
        "name": "System.Web.cmeykh7afr.dll",
        "hash": "sha256-fBkPFItL5HHtaqkHD1EGB3bx2epjbIpMyyUKchdCn6M=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Windows.dll",
        "name": "System.Windows.55kw1jenyn.dll",
        "hash": "sha256-B2WsIZwwQkC9trsrWztFn45qOQ9hIghVf/Cm25L514I=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.dll",
        "name": "System.Xml.673es23k0x.dll",
        "hash": "sha256-TuSUaxelsgei6rDSTGxExRvPCGrdWaFUkwYuHyIVG3Y=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.Linq.dll",
        "name": "System.Xml.Linq.azvtlyx8hj.dll",
        "hash": "sha256-2qv2IbfPXAye9jlUl/gwGOfPrZVsLXjZmxVvccifH+o=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.ReaderWriter.dll",
        "name": "System.Xml.ReaderWriter.vq4lqkixii.dll",
        "hash": "sha256-uWriiTqsR33GTI39TqMkkC6sJLB9EdW48ofgGc+QJuw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.Serialization.dll",
        "name": "System.Xml.Serialization.ejlbgfcigc.dll",
        "hash": "sha256-NtIYoj4JTYq4G+Qt0o52F06FUTO4O0TMQmwVgdb43GM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XDocument.dll",
        "name": "System.Xml.XDocument.nn0f763h5i.dll",
        "hash": "sha256-E2C6jXKfdVMjrwaTZHaas+umMJtb97IwaqK/ZRMET8E=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XPath.dll",
        "name": "System.Xml.XPath.1cvnig43mu.dll",
        "hash": "sha256-n2+Cchb+7/nVMJ9zEMa4D+K+6yfZJBSrB10IAVejncw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XPath.XDocument.dll",
        "name": "System.Xml.XPath.XDocument.b2f7866xxj.dll",
        "hash": "sha256-A8g/zM3bSHNbTDw2Ux73pmvBJ1yQIly28m68JZhH2eg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XmlDocument.dll",
        "name": "System.Xml.XmlDocument.4eygmv2agj.dll",
        "hash": "sha256-5JvdBwjQbwSKersUr8KcehlGz9TVK3Ov+z7N0wJfhNo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XmlSerializer.dll",
        "name": "System.Xml.XmlSerializer.hbhybqdg04.dll",
        "hash": "sha256-0xAhAmc/nUPiCSYGM2bIhXQtJpxOTADlzLTGJgkZ7mA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.dll",
        "name": "System.y9e9egc1wd.dll",
        "hash": "sha256-hr5+4Mo1Hiodztm2VR4cIUjWjgTohiE+TuH7IYzFOkA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "WindowsBase.dll",
        "name": "WindowsBase.a04qyhi5fq.dll",
        "hash": "sha256-NsPzL2qXvQPym4DHA5vATHMUPbe/4GKq5g6MpxLNvjg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "mscorlib.dll",
        "name": "mscorlib.72sqldkmmv.dll",
        "hash": "sha256-caafl62mqVioQosHL0TZe5hoxOJ41fko8j+uk5QmwGs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "netstandard.dll",
        "name": "netstandard.8ypws8hppi.dll",
        "hash": "sha256-oKiAwAW/ezC4yp96CVtvmiaSM5eU9xMAGRCE1DUhe50=",
        "cache": "force-cache"
      }
    ],
    "satelliteResources": {
      "cs": [
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.Features.resources.tg9twwf6k2.dll",
          "hash": "sha256-0xqvgjlrDeamVnhUbZ927bQ5leb3Po6tBzZBYW+8UGY=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.yt3jebil4s.dll",
          "hash": "sha256-JtQ3vRXgY0dPoCxBVqNtewr1PZ5aNUQRlqApNzvfO6o=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.resources.h5txsasn5k.dll",
          "hash": "sha256-K2qdjSN0yOrsWHCowNZn/TaOzf55vlFfNivO2hjAHmE=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Features.resources.dll",
          "name": "Microsoft.CodeAnalysis.Features.resources.gcx3rp75lq.dll",
          "hash": "sha256-cIVxVf9Xmg/e6NRO45dbD/JDAvX9ElPDG+bbw8hwaVw=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Scripting.resources.dll",
          "name": "Microsoft.CodeAnalysis.Scripting.resources.ytrbg6e683.dll",
          "hash": "sha256-WlbxKw4HJULQWKrbuV3bz5nu9jhu1yNucvntljeI/LE=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Workspaces.resources.dll",
          "name": "Microsoft.CodeAnalysis.Workspaces.resources.dpj3oe6keq.dll",
          "hash": "sha256-gfbbyrfgJLBi2pHbZo+4sgGVBsj59xZaG6GEdtVpsMg=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.resources.dll",
          "name": "Microsoft.CodeAnalysis.resources.28esv6a9sy.dll",
          "hash": "sha256-/pEWiTWFOOHnaZ8JydbX++w9m+ttbOocDJW/P+41cgU=",
          "cache": "force-cache"
        }
      ],
      "de": [
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.Features.resources.lr9yoiwm9h.dll",
          "hash": "sha256-8YXHClvkRQJZKO5qlHqoqU3EWfMevzw0J6tIV2Jzq5g=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.xx7395zkz1.dll",
          "hash": "sha256-9VdBFCFJHrVtE/DSGGjLYbAVmTYldYBiks52Or7r6rs=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.resources.dysuol6opr.dll",
          "hash": "sha256-FdpRHEGB66UHDeP4pK0IMUor73Ixh1LINF2BoOFiLfE=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Features.resources.dll",
          "name": "Microsoft.CodeAnalysis.Features.resources.7e7malsg0a.dll",
          "hash": "sha256-EbMOBNshWYnHZ1vC4lpZlt2qX6PkGUiyJBVRyMwIoiU=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Scripting.resources.dll",
          "name": "Microsoft.CodeAnalysis.Scripting.resources.sxhkcg7y57.dll",
          "hash": "sha256-0FNLaxdGY+Eayd9YX9d4TFAqMYH2nWirupzgUKAoEnc=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Workspaces.resources.dll",
          "name": "Microsoft.CodeAnalysis.Workspaces.resources.lbborngcy8.dll",
          "hash": "sha256-UQaIlMYNhqxIW9DXOR3f+oepmAd4wJO4nCXqkfEFTM4=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.resources.dll",
          "name": "Microsoft.CodeAnalysis.resources.tx2oxdmf3d.dll",
          "hash": "sha256-H6aO1fpjKIi2zTjkdDfYgqjNCBlqk+W2tFfcLuVKa7I=",
          "cache": "force-cache"
        }
      ],
      "es": [
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.Features.resources.aowteck1v3.dll",
          "hash": "sha256-Vrf9oIHUM2LTOcIoSkdFWwFequfh4cxWdTHt6rCfFL4=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.vjw7b3c39x.dll",
          "hash": "sha256-i/dLUZLL3iQ0DskIr/U/l/9pKMrd43Qnw+abXVs2drI=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.resources.u1kdnbbcyq.dll",
          "hash": "sha256-wtkGQnLIzO8jqbbvb1sHRgvRPCYd+wAs/TKxzR9ZrZc=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Features.resources.dll",
          "name": "Microsoft.CodeAnalysis.Features.resources.170a5fcx81.dll",
          "hash": "sha256-gx+iOljTF//BVV2p51Q8VAXZGOHOHS7reNzxn8MoFHQ=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Scripting.resources.dll",
          "name": "Microsoft.CodeAnalysis.Scripting.resources.dkrjx4eb0v.dll",
          "hash": "sha256-TQPif2M7zmsDIKUZ9Fu5QEJzS/mQvb3bBdEo2GPvZXI=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Workspaces.resources.dll",
          "name": "Microsoft.CodeAnalysis.Workspaces.resources.ffgm0xlhib.dll",
          "hash": "sha256-VU9EUhmX0bP1br3oXRiI9WTqbZGKUbEISJqXShvnwZ8=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.resources.dll",
          "name": "Microsoft.CodeAnalysis.resources.0sg8jrisjr.dll",
          "hash": "sha256-EJrWZmqDSxzbLenuPt3G4bTMgX2BFtPxajy0G2RIQBE=",
          "cache": "force-cache"
        }
      ],
      "fr": [
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.Features.resources.amxqd4ezxf.dll",
          "hash": "sha256-TnaCAc6aLreVDzIe6KUIEY/hERhlkIwZT9k68w7NRyM=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.ph5lii46cl.dll",
          "hash": "sha256-81byIbOmHLT6bOUFkmIwTtvzSu0TFuHUVIh0B/k1T/Y=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.resources.jbgtpg3x3m.dll",
          "hash": "sha256-32AO01FhOXYo7iUJw4vkjSVX5dffTtfx/0C1RouvXH8=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Features.resources.dll",
          "name": "Microsoft.CodeAnalysis.Features.resources.2ysin49rho.dll",
          "hash": "sha256-CroOjxY/nVYjjRNOIW+Gx95OHj1p+fvZC+nu7mK/F08=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Scripting.resources.dll",
          "name": "Microsoft.CodeAnalysis.Scripting.resources.aiwhsuoacm.dll",
          "hash": "sha256-0pI1DnHaO/cAuOib4iQoAxeMsfnUfJ1owDYbcOQyxcQ=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Workspaces.resources.dll",
          "name": "Microsoft.CodeAnalysis.Workspaces.resources.oh2rxk2oyh.dll",
          "hash": "sha256-pAmg4qblnDKzgiyZk4vDBUZLfhY+VuxeTH4kqqeTkA0=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.resources.dll",
          "name": "Microsoft.CodeAnalysis.resources.qbtk1e79ly.dll",
          "hash": "sha256-dhrEwTp4vIArt2a6kMfuUF/LTh0/3KE7k9y1kxmly1c=",
          "cache": "force-cache"
        }
      ],
      "it": [
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.Features.resources.n2paw57yfy.dll",
          "hash": "sha256-b+EVmiK/WwomyfjcjXga8IpjSsmpEWEwmXK7j1gIel8=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.zo2640i8kb.dll",
          "hash": "sha256-I/d78JjexPRGXRytKYoJ7Ncm5tT+oBAisbSTVKRN+FU=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.resources.4jrkrib9so.dll",
          "hash": "sha256-oMQLDBzXwiL+fpyEFiL09SFotqRrwdSVvjFzCG/v6o8=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Features.resources.dll",
          "name": "Microsoft.CodeAnalysis.Features.resources.3ig42st9gr.dll",
          "hash": "sha256-i24BRfeMrSAHRaVdLvQ6+CMYldlp+PfSluDDV6eMqxo=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Scripting.resources.dll",
          "name": "Microsoft.CodeAnalysis.Scripting.resources.tiquxvl6fq.dll",
          "hash": "sha256-O7H5QXAOMceq6Tvo5W5mLCbOU52J5RND5+6hWmGNYzo=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Workspaces.resources.dll",
          "name": "Microsoft.CodeAnalysis.Workspaces.resources.jk3idxujw4.dll",
          "hash": "sha256-k9NrjIGJh3BTBRfHyHeTr0lpl2HNP7/NWPf4RO5nipg=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.resources.dll",
          "name": "Microsoft.CodeAnalysis.resources.ygmq38xval.dll",
          "hash": "sha256-vjhCy56EbHr/8tYhzFp4n5aNVbrk2cnfIjAjgw77JgI=",
          "cache": "force-cache"
        }
      ],
      "ja": [
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.Features.resources.2h0luaascm.dll",
          "hash": "sha256-WiTcZOegxiiRVlzKABMffXx6vjAdZnc5LNAQ4ozipSs=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.vf6ltskoyy.dll",
          "hash": "sha256-24CNOx20a5Rz6TNqreDMHipWMZG7EcQEmncfo1o4N3M=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.resources.v75ub0zscy.dll",
          "hash": "sha256-65CBv0rfqZNCZVBuUkaQsM0V8iAOQ3Wp9sX7r/bKd5s=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Features.resources.dll",
          "name": "Microsoft.CodeAnalysis.Features.resources.97tnoznv3m.dll",
          "hash": "sha256-laeL4Q0uNaoNyZvQBPfUIoQak/EKS/StjfOkdXtaz1E=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Scripting.resources.dll",
          "name": "Microsoft.CodeAnalysis.Scripting.resources.x5i850k14x.dll",
          "hash": "sha256-9bKJDNszI1V21TNnXzS1CasT5YKWQYPj6eYMX4D98OI=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Workspaces.resources.dll",
          "name": "Microsoft.CodeAnalysis.Workspaces.resources.mcaxku72wc.dll",
          "hash": "sha256-pm5tRItfHow4r00Xtp2UlO3cxYk82B/yzUHLOEycA+k=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.resources.dll",
          "name": "Microsoft.CodeAnalysis.resources.spitmq3zsb.dll",
          "hash": "sha256-AHL1xs359nYwRqAmK5eddCOOlFimfjWH5bzhVYEzHmk=",
          "cache": "force-cache"
        }
      ],
      "ko": [
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.Features.resources.pxqvn75gzc.dll",
          "hash": "sha256-4yLfEI+79knhlMq1aUJ1SE5AtpLtkqKjPxevLAcBXUM=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.uwmn0y1rn1.dll",
          "hash": "sha256-vmullM7B6+ox5yHugwL+YO7gi0ybEVQK4lFicNt4k8Q=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.resources.6dhplk28jh.dll",
          "hash": "sha256-KkmYIBAOgzt1rGxd/zHHiyzM+vKQeNAgE6mGmXDBstQ=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Features.resources.dll",
          "name": "Microsoft.CodeAnalysis.Features.resources.6reihpq98i.dll",
          "hash": "sha256-MrAyb5JNlzp4yYpsGZIYrf7Y6f/u+grJki661+6L0eQ=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Scripting.resources.dll",
          "name": "Microsoft.CodeAnalysis.Scripting.resources.guna751acn.dll",
          "hash": "sha256-yD0q8Xb9xLbDO6Jm1gVFGO2LhZfgn7q41GWN5x/2SWg=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Workspaces.resources.dll",
          "name": "Microsoft.CodeAnalysis.Workspaces.resources.xolx3wjxki.dll",
          "hash": "sha256-Eb+JU0noG8kF/LP9Adwc7mpiMFe0mn/S/cbyt0SpUms=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.resources.dll",
          "name": "Microsoft.CodeAnalysis.resources.b0rgz18y7k.dll",
          "hash": "sha256-RWwUg8f37kCWOL44LiGgu8CSYDb5p6XiWH0TYn7ZblE=",
          "cache": "force-cache"
        }
      ],
      "pl": [
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.Features.resources.tefkvq5ijq.dll",
          "hash": "sha256-BYLEjUMkqEVXHePUwsE71kxbQ8jefPeTIppT5PpsYt4=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.jj9r96pzff.dll",
          "hash": "sha256-DzrQ14gfaQhHkwiB6cQtUiRSm4d6S+DD7wP3elEMMxo=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.resources.ivz2jbkwe1.dll",
          "hash": "sha256-4p4aecePRTLImsbjn7qwIyj5mTGTjnzwSJbOc7wv/nk=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Features.resources.dll",
          "name": "Microsoft.CodeAnalysis.Features.resources.qmhzzcbvwd.dll",
          "hash": "sha256-AmfdyElOQmcdkHRJ7YvKp21YY81t4yhP01FKLCXyiIo=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Scripting.resources.dll",
          "name": "Microsoft.CodeAnalysis.Scripting.resources.avc32picb9.dll",
          "hash": "sha256-5sk4z4Fzzp8weN+d5PgXGES8jLyadg8rPJjb7xFSjro=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Workspaces.resources.dll",
          "name": "Microsoft.CodeAnalysis.Workspaces.resources.oza6y88737.dll",
          "hash": "sha256-JD+87M48LfMtHxAzDjfXq8ulf6c4Hoqw5hfJdN7I3cI=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.resources.dll",
          "name": "Microsoft.CodeAnalysis.resources.zi1rosncd1.dll",
          "hash": "sha256-hUdFYO9ETBOJdNQ68JUW8zUF1K9Lu1PCACJkAR8SC20=",
          "cache": "force-cache"
        }
      ],
      "pt-BR": [
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.Features.resources.nhdsn41sj6.dll",
          "hash": "sha256-S0o1YSiHFjJfoKfdc3u477jx4FtKQsLnAKhwXu6nHGs=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.r98wl0xkvm.dll",
          "hash": "sha256-3zYGuaQbP2NehTPHFaDNdj6T2Cjkx6GQxKU9DFG/dU4=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.resources.hr428280bt.dll",
          "hash": "sha256-nVSr6TuYVZEyQRBcCDaEyopwcm4GUMMJVHF8GZ2HNC8=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Features.resources.dll",
          "name": "Microsoft.CodeAnalysis.Features.resources.buz5s1jhgw.dll",
          "hash": "sha256-s/T8F4dFSysjgqPfuG5wMUmHnrUfhencP81aMa72v4o=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Scripting.resources.dll",
          "name": "Microsoft.CodeAnalysis.Scripting.resources.jv2hi90k7w.dll",
          "hash": "sha256-McEiUEPs1krNqEON/4KJrmB3KXLyHi3G1sn2fTh1Sb0=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Workspaces.resources.dll",
          "name": "Microsoft.CodeAnalysis.Workspaces.resources.hcw88ks6oy.dll",
          "hash": "sha256-wW1z4Z2rfg0AAZ2/2j1NtX1Lqg9Qgx7sOST7G1jtkFw=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.resources.dll",
          "name": "Microsoft.CodeAnalysis.resources.9261nmyg9x.dll",
          "hash": "sha256-D3ymKRptf1767GDEfukB5vY4n5XD3M05ax5yTPg0X6w=",
          "cache": "force-cache"
        }
      ],
      "ru": [
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.Features.resources.n8fhrqin62.dll",
          "hash": "sha256-Z+rZgAqxgqtKghqJC3JFPnjSCNjNuSjguqOVWWtA+cQ=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.6iaz86l4ro.dll",
          "hash": "sha256-ErBZw5XpYTLLAMAD1FzQt3QaPMVsK1xtP935J8z1PWI=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.resources.l853gd6ifc.dll",
          "hash": "sha256-RYFQotJ3ph9Rqiwh2yh/WK2U2SST1VmDfjg/inp+ggc=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Features.resources.dll",
          "name": "Microsoft.CodeAnalysis.Features.resources.fmw0xdgicy.dll",
          "hash": "sha256-JzqOWhBAq0JNiUHz0qZ1IJVHERrvCxpYf0yOTj/sEAE=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Scripting.resources.dll",
          "name": "Microsoft.CodeAnalysis.Scripting.resources.6i81xs1xf0.dll",
          "hash": "sha256-sh2j8WY595Tk75Je0sW4ysZ34RkrdiuG2HXOK72fHuk=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Workspaces.resources.dll",
          "name": "Microsoft.CodeAnalysis.Workspaces.resources.x1mb89pkm3.dll",
          "hash": "sha256-ZaAcsAuK0WM4PJvJbegTZp3I5nnRAa8Gn32rOPzDhco=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.resources.dll",
          "name": "Microsoft.CodeAnalysis.resources.6wqsq3letc.dll",
          "hash": "sha256-Jo2f7SgY4A9tnaxHwyiyoLJv6QadIVu18Ez/PzgE7nw=",
          "cache": "force-cache"
        }
      ],
      "tr": [
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.Features.resources.pxjst44x4a.dll",
          "hash": "sha256-E18reR/htYXlHD/d6T6lUu7b+92+hP60H4Z72ldDSLY=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.tjs62jvujd.dll",
          "hash": "sha256-CbRX7W98D6ZRcMt9HlDhJr1YZBN3lNM+c/bjVPc7U7E=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.resources.m9lsa1aijg.dll",
          "hash": "sha256-Vpe2JsS/OmaRsqu5PCHxNHGymrDifuzakp36mmTJYt4=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Features.resources.dll",
          "name": "Microsoft.CodeAnalysis.Features.resources.yliw9ic7b3.dll",
          "hash": "sha256-NteswYetp6gsnRxpFxVDgigO9TygdwW6eGC+5WgaDKk=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Scripting.resources.dll",
          "name": "Microsoft.CodeAnalysis.Scripting.resources.aup2ffuo0q.dll",
          "hash": "sha256-UtpjawVwVw5a9BHPShZqV7iqtHc9NR1T51yc5X+s8A4=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Workspaces.resources.dll",
          "name": "Microsoft.CodeAnalysis.Workspaces.resources.zoiezviisx.dll",
          "hash": "sha256-I9VE2sb9NANU9aPDML1vPfOLlemIpLDgW88v7w3f/aE=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.resources.dll",
          "name": "Microsoft.CodeAnalysis.resources.adju8xyl8q.dll",
          "hash": "sha256-jkldRjMxaZcjWriYj012CzT77jAWrZ0WKLKkAIIVe28=",
          "cache": "force-cache"
        }
      ],
      "zh-Hans": [
        {
          "virtualPath": "AvaloniaEdit.resources.dll",
          "name": "AvaloniaEdit.resources.9odwdc0yqa.dll",
          "hash": "sha256-x5VFwV5NBa31JRdY/FDUbBYpDGuqVt4JtGuAgxiGs9w=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.Features.resources.mu1jmutm6l.dll",
          "hash": "sha256-Hu4op4j8GHtWieJYVCqcVdXk+JbgZOI7sa6NPwTZ2n0=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.w1d77nuhht.dll",
          "hash": "sha256-1IAo09pygnAl7va3RsN1teYIepsZ22rGyPL1Aba7tQ8=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.resources.6cottfn9no.dll",
          "hash": "sha256-ilbyAIbibJzGAHSeR8sa1paMtvvvhKOz7dgtlzxDuSk=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Features.resources.dll",
          "name": "Microsoft.CodeAnalysis.Features.resources.mhefhlo3z8.dll",
          "hash": "sha256-GlwVRIgiapEGm2zH+BblJXCKSqECog/94ezaO4pIzkU=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Scripting.resources.dll",
          "name": "Microsoft.CodeAnalysis.Scripting.resources.02a4j9z0b8.dll",
          "hash": "sha256-6PITf2GFMQAl/GOaDh0yW+A2T26/CgmWZ+ErmukZ+Z4=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Workspaces.resources.dll",
          "name": "Microsoft.CodeAnalysis.Workspaces.resources.3dgggxpag7.dll",
          "hash": "sha256-1/oHQ+1HxEgf2qnc59zR4HqAFAZmu4Ke3yi8AJJ/Kb0=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.resources.dll",
          "name": "Microsoft.CodeAnalysis.resources.lgdbvsa4ld.dll",
          "hash": "sha256-G9i6fzPCr4XUIfqw5FG1fRddBZb6dxu36PJHNIPs/4c=",
          "cache": "force-cache"
        }
      ],
      "zh-Hant": [
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.Features.resources.halzpbrgun.dll",
          "hash": "sha256-dzScwUjpfceby3AYQdvNOg6did3erlPY8a6Y/bhmWfs=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.kzcwuok1lp.dll",
          "hash": "sha256-QDhUR47kAmPhv1BXaBHs7sqOzWs7yweiCLsonazT+7I=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.resources.dll",
          "name": "Microsoft.CodeAnalysis.CSharp.resources.l83nx83hlx.dll",
          "hash": "sha256-2z5kLkBXpvn9jxDBjTrxWP7i+9ql5ZPbDAD0QfDkvNc=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Features.resources.dll",
          "name": "Microsoft.CodeAnalysis.Features.resources.8c6z91wsgm.dll",
          "hash": "sha256-KKjMszR3d5jlvsiRoEv0L4WI2XsSx76+l6bStofg7ek=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Scripting.resources.dll",
          "name": "Microsoft.CodeAnalysis.Scripting.resources.dhhozoig7d.dll",
          "hash": "sha256-fw4gBIQBZqKzlKLdmDi6XF6mOl/RbNMqdOWUfsjwNj4=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Workspaces.resources.dll",
          "name": "Microsoft.CodeAnalysis.Workspaces.resources.0rmjwmn3fn.dll",
          "hash": "sha256-7GFszVHbDJI5G3mF8Z8CqA6qh9UYwULfSAclRFRYA7s=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.resources.dll",
          "name": "Microsoft.CodeAnalysis.resources.aw1he4dcb7.dll",
          "hash": "sha256-2lGD40/LnxBR62ETElx9S3QOkD5B8952U8VPP4EZUR0=",
          "cache": "force-cache"
        }
      ]
    }
  },
  "debugLevel": 0,
  "globalizationMode": "sharded",
  "runtimeConfig": {
    "runtimeOptions": {
      "configProperties": {
        "System.Diagnostics.Debugger.IsSupported": false,
        "System.Diagnostics.Metrics.Meter.IsSupported": false,
        "System.Diagnostics.Tracing.EventSource.IsSupported": false,
        "System.Globalization.Invariant": false,
        "System.TimeZoneInfo.Invariant": false,
        "System.Linq.Enumerable.IsSizeOptimized": true,
        "System.Net.Http.EnableActivityPropagation": false,
        "System.Net.Http.WasmEnableStreamingResponse": true,
        "System.Net.SocketsHttpHandler.Http3Support": false,
        "System.Reflection.Metadata.MetadataUpdater.IsSupported": false,
        "System.Resources.UseSystemResourceKeys": true,
        "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization": false,
        "System.Text.Encoding.EnableUnsafeUTF7Encoding": false
      }
    }
  }
}/*json-end*/);export{ht as default,gt as dotnet,pt as exit};
