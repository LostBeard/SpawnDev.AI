(function () {
    'use strict';

    var __spawnjsBundleUrl = (typeof document !== 'undefined' && document.currentScript ? document.currentScript.src : typeof self !== 'undefined' && self.location ? self.location.href : typeof location !== 'undefined' ? location.href : '');


    function _mergeNamespaces(n, m) {
        m.forEach(function (e) {
            e && typeof e !== 'string' && !Array.isArray(e) && Object.keys(e).forEach(function (k) {
                if (k !== 'default' && !(k in n)) {
                    var d = Object.getOwnPropertyDescriptor(e, k);
                    Object.defineProperty(n, k, d.get ? d : {
                        enumerable: true,
                        get: function () { return e[k]; }
                    });
                }
            });
        });
        return Object.freeze(n);
    }

    // Holds events that happen while Blazor WASM is loading

    (function () {
        // Optional verbose logger. Silent unless globalThis.spawnjsVerbose is truthy.
        // (Previously this file referenced an undefined `consoleLog`, which threw a
        // ReferenceError the moment an event was actually missed - breaking the hold.)
        function consoleLog() {
            if (!globalThis.spawnjsVerbose) return;
            console.log.apply(console, arguments);
        }
        var globalThisTypeName = globalThis.constructor?.name;
        if (globalThisTypeName == 'SharedWorkerGlobalScope') {
            // important for SharedWorker
            // catch any incoming connections that happen while .Net is loading
            let _missedConnections = [];
            globalThis.takeOverOnConnectEvent = function (newConnectFunction) {
                var tmp = _missedConnections;
                _missedConnections = [];
                globalThis.onconnect = newConnectFunction;
                return tmp;
            };
            globalThis.onconnect = function (e) {
                _missedConnections.push(e.ports[0]);
            };
        } else if (globalThisTypeName == 'ServiceWorkerGlobalScope') {
            var isExtensionScope = globalThis.location?.href && globalThis.location.href.indexOf('-extension://') !== -1;
            if (!isExtensionScope) {
                // .Net Wasm startup is async. This holds the synchronously fired ewvents for so they are available when .Net Wasm starts
                // !isBackgroundExtensionScript is used to prevent this from attaching events when i na browser extension service worker because it will
                // cause script suspend/resume to fail withotu ANY error.
                let holdEvents = true;
                let missedServiceWorkerEvents = [];
                function handleMissedEvent(e) {
                    if (!holdEvents) return;
                    consoleLog('ServiceWorker missed event:', e.type, e);
                    if (e.respondWith) {
                        // fetch and canmakepayment ExtendableEvents use respondWith
                        var responsePromise = new Promise(function (resolve, reject) {
                            e.responseResolve = resolve;
                            e.responseReject = reject;
                        });
                        e.respondWith(responsePromise);
                    } else if (e.waitUntil) {
                        // all other ExtendableEvents use waitUntil
                        var waitUntilPromise = new Promise(function (resolve, reject) {
                            e.waitResolve = resolve;
                            e.waitReject = reject;
                        });
                        e.waitUntil(waitUntilPromise);
                    }
                    missedServiceWorkerEvents.push(e);
                }
                globalThis.addEventListener('activate', handleMissedEvent);
                globalThis.addEventListener('backgroundfetchabort', handleMissedEvent);
                globalThis.addEventListener('backgroundfetchclick', handleMissedEvent);
                globalThis.addEventListener('backgroundfetchfail', handleMissedEvent);
                globalThis.addEventListener('backgroundfetchsuccess', handleMissedEvent);
                globalThis.addEventListener('canmakepayment', handleMissedEvent);
                globalThis.addEventListener('contentdelete', handleMissedEvent);
                globalThis.addEventListener('cookiechange', handleMissedEvent);
                globalThis.addEventListener('fetch', handleMissedEvent);
                globalThis.addEventListener('install', handleMissedEvent);
                globalThis.addEventListener('message', handleMissedEvent);
                globalThis.addEventListener('messageerror', handleMissedEvent);
                globalThis.addEventListener('notificationclick', handleMissedEvent);
                globalThis.addEventListener('notificationclose', handleMissedEvent);
                globalThis.addEventListener('paymentrequest', handleMissedEvent);
                globalThis.addEventListener('periodicsync', handleMissedEvent);
                globalThis.addEventListener('push', handleMissedEvent);
                globalThis.addEventListener('pushsubscriptionchange', handleMissedEvent);
                globalThis.addEventListener('sync', handleMissedEvent);
                // This method will be called by Blazor WASM when it starts up to collect missed events and handle them
                globalThis.GetMissedServiceWorkerEvents = function () {
                    holdEvents = false;
                    var ret = missedServiceWorkerEvents;
                    missedServiceWorkerEvents = [];
                    return ret;
                };
            }
        }
    })();

    //! Licensed to the .NET Foundation under one or more agreements.
    //! The .NET Foundation licenses this file to you under the MIT license.
    var e$1="10.0.11",t$1="Release",n$1=false;const r$1=[[true,"mono_wasm_register_root","number",["number","number","string"]],[true,"mono_wasm_deregister_root",null,["number"]],[true,"mono_wasm_string_get_data_ref",null,["number","number","number","number"]],[true,"mono_wasm_set_is_debugger_attached","void",["bool"]],[true,"mono_wasm_send_dbg_command","bool",["number","number","number","number","number"]],[true,"mono_wasm_send_dbg_command_with_parms","bool",["number","number","number","number","number","number","string"]],[true,"mono_wasm_setenv",null,["string","string"]],[true,"mono_wasm_parse_runtime_options",null,["number","number"]],[true,"mono_wasm_strdup","number",["string"]],[true,"mono_background_exec",null,[]],[true,"mono_wasm_ds_exec",null,[]],[true,"mono_wasm_execute_timer",null,[]],[true,"mono_wasm_load_icu_data","number",["number"]],[false,"mono_wasm_add_assembly","number",["string","number","number"]],[true,"mono_wasm_add_satellite_assembly","void",["string","string","number","number"]],[false,"mono_wasm_load_runtime",null,["number","number","number","number"]],[true,"mono_wasm_change_debugger_log_level","void",["number"]],[true,"mono_wasm_assembly_load","number",["string"]],[true,"mono_wasm_assembly_find_class","number",["number","string","string"]],[true,"mono_wasm_assembly_find_method","number",["number","string","number"]],[true,"mono_wasm_string_from_utf16_ref","void",["number","number","number"]],[true,"mono_wasm_intern_string_ref","void",["number"]],[false,"mono_wasm_exit","void",["number"]],[true,"mono_wasm_getenv","number",["string"]],[true,"mono_wasm_set_main_args","void",["number","number"]],[()=>!ct$1.emscriptenBuildOptions.enableAotProfiler,"mono_wasm_profiler_init_aot","void",["string"]],[()=>!ct$1.emscriptenBuildOptions.enableDevToolsProfiler,"mono_wasm_profiler_init_browser_devtools","void",["string"]],[()=>!ct$1.emscriptenBuildOptions.enableLogProfiler,"mono_wasm_profiler_init_log","void",["string"]],[false,"mono_wasm_exec_regression","number",["number","string"]],[false,"mono_wasm_invoke_jsexport","void",["number","number"]],[true,"mono_wasm_write_managed_pointer_unsafe","void",["number","number"]],[true,"mono_wasm_copy_managed_pointer","void",["number","number"]],[true,"mono_wasm_i52_to_f64","number",["number","number"]],[true,"mono_wasm_u52_to_f64","number",["number","number"]],[true,"mono_wasm_f64_to_i52","number",["number","number"]],[true,"mono_wasm_f64_to_u52","number",["number","number"]],[true,"mono_wasm_method_get_name","number",["number"]],[true,"mono_wasm_method_get_name_ex","number",["number"]],[true,"mono_wasm_method_get_full_name","number",["number"]],[true,"mono_wasm_gc_lock","void",[]],[true,"mono_wasm_gc_unlock","void",[]],[true,"mono_wasm_get_i32_unaligned","number",["number"]],[true,"mono_wasm_get_f32_unaligned","number",["number"]],[true,"mono_wasm_get_f64_unaligned","number",["number"]],[true,"mono_wasm_read_as_bool_or_null_unsafe","number",["number"]],[true,"mono_jiterp_trace_bailout","void",["number"]],[true,"mono_jiterp_get_trace_bailout_count","number",["number"]],[true,"mono_jiterp_value_copy","void",["number","number","number"]],[true,"mono_jiterp_get_member_offset","number",["number"]],[true,"mono_jiterp_encode_leb52","number",["number","number","number"]],[true,"mono_jiterp_encode_leb64_ref","number",["number","number","number"]],[true,"mono_jiterp_encode_leb_signed_boundary","number",["number","number","number"]],[true,"mono_jiterp_write_number_unaligned","void",["number","number","number"]],[true,"mono_jiterp_type_is_byref","number",["number"]],[true,"mono_jiterp_get_size_of_stackval","number",[]],[true,"mono_jiterp_parse_option","number",["string"]],[true,"mono_jiterp_get_options_as_json","number",[]],[true,"mono_jiterp_get_option_as_int","number",["string"]],[true,"mono_jiterp_get_options_version","number",[]],[true,"mono_jiterp_adjust_abort_count","number",["number","number"]],[true,"mono_jiterp_register_jit_call_thunk","void",["number","number"]],[true,"mono_jiterp_type_get_raw_value_size","number",["number"]],[true,"mono_jiterp_get_signature_has_this","number",["number"]],[true,"mono_jiterp_get_signature_return_type","number",["number"]],[true,"mono_jiterp_get_signature_param_count","number",["number"]],[true,"mono_jiterp_get_signature_params","number",["number"]],[true,"mono_jiterp_type_to_ldind","number",["number"]],[true,"mono_jiterp_type_to_stind","number",["number"]],[true,"mono_jiterp_imethod_to_ftnptr","number",["number"]],[true,"mono_jiterp_debug_count","number",[]],[true,"mono_jiterp_get_trace_hit_count","number",["number"]],[true,"mono_jiterp_get_polling_required_address","number",[]],[true,"mono_jiterp_get_rejected_trace_count","number",[]],[true,"mono_jiterp_boost_back_branch_target","void",["number"]],[true,"mono_jiterp_is_imethod_var_address_taken","number",["number","number"]],[true,"mono_jiterp_get_opcode_value_table_entry","number",["number"]],[true,"mono_jiterp_get_simd_intrinsic","number",["number","number"]],[true,"mono_jiterp_get_simd_opcode","number",["number","number"]],[true,"mono_jiterp_get_arg_offset","number",["number","number","number"]],[true,"mono_jiterp_get_opcode_info","number",["number","number"]],[true,"mono_wasm_is_zero_page_reserved","number",[]],[true,"mono_jiterp_is_special_interface","number",["number"]],[true,"mono_jiterp_initialize_table","void",["number","number","number"]],[true,"mono_jiterp_allocate_table_entry","number",["number"]],[true,"mono_jiterp_get_interp_entry_func","number",["number"]],[true,"mono_jiterp_get_counter","number",["number"]],[true,"mono_jiterp_modify_counter","number",["number","number"]],[true,"mono_jiterp_tlqueue_next","number",["number"]],[true,"mono_jiterp_tlqueue_add","number",["number","number"]],[true,"mono_jiterp_tlqueue_clear","void",["number"]],[true,"mono_jiterp_begin_catch","void",["number"]],[true,"mono_jiterp_end_catch","void",[]],[true,"mono_interp_pgo_load_table","number",["number","number"]],[true,"mono_interp_pgo_save_table","number",["number","number"]],[()=>!ct$1.emscriptenBuildOptions.enableEventPipe&&!ct$1.emscriptenBuildOptions.enableDevToolsProfiler,"mono_jiterp_prof_enter","void",["number","number"]],[()=>!ct$1.emscriptenBuildOptions.enableEventPipe&&!ct$1.emscriptenBuildOptions.enableDevToolsProfiler,"mono_jiterp_prof_samplepoint","void",["number","number"]],[()=>!ct$1.emscriptenBuildOptions.enableEventPipe&&!ct$1.emscriptenBuildOptions.enableDevToolsProfiler,"mono_jiterp_prof_leave","void",["number","number"]]],o$1={},s$1=o$1,a$1=o$1,i$1=["void","number",null];function c$1(e,t,n,r){let o=void 0===r&&i$1.indexOf(t)>=0&&(!n||n.every((e=>i$1.indexOf(e)>=0)))&&Ke$1.wasmExports?Ke$1.wasmExports[e]:void 0;if(o&&n&&o.length!==n.length&&(He$1(`argument count mismatch for cwrap ${e}`),o=void 0),"function"!=typeof o&&(o=Ke$1.cwrap(e,t,n,r)),"function"!=typeof o)throw new Error(`cwrap ${e} not found or not a function`);return o}const l$1=0,p$1=0,u$1=0,d$1=BigInt("9223372036854775807"),f$1=BigInt("-9223372036854775808");function _$1(e){return Ke$1._malloc(e)>>>0}function m$1(e){Ke$1._free(e);}function h$1(e,t,n){if(!Number.isSafeInteger(e))throw new Error(`Assert failed: Value is not an integer: ${e} (${typeof e})`);if(!(e>=t&&e<=n))throw new Error(`Assert failed: Overflow: value ${e} is out of ${t} ${n} range`)}function g$1(e,t){K$1().fill(0,e,e+t);}function b$1(e,t){const n=!!t;"number"==typeof t&&h$1(t,0,1),Ke$1.HEAP32[e>>>2]=n?1:0;}function y$1(e,t){const n=!!t;"number"==typeof t&&h$1(t,0,1),Ke$1.HEAPU8[e]=n?1:0;}function w$1(e,t){h$1(t,0,255),Ke$1.HEAPU8[e]=t;}function k$1(e,t){h$1(t,0,65535),Ke$1.HEAPU16[e>>>1]=t;}function S$1(e,t){h$1(t,0,4294967295),Ke$1.HEAPU32[e>>>2]=t;}function v$1(e,t){h$1(t,-128,127),Ke$1.HEAP8[e]=t;}function U$1(e,t){h$1(t,-32768,32767),Ke$1.HEAP16[e>>>1]=t;}function T$1(e,t){h$1(t,-2147483648,2147483647),Ke$1.HEAP32[e>>>2]=t;}function E$1(e){if(0!==e)switch(e){case 1:throw new Error("value was not an integer");case 2:throw new Error("value out of range");default:throw new Error("unknown internal error")}}function x$1(e,t){if(!Number.isSafeInteger(t))throw new Error(`Assert failed: Value is not a safe integer: ${t} (${typeof t})`);E$1(o$1.mono_wasm_f64_to_i52(e,t));}function I$1(e,t){if(!Number.isSafeInteger(t))throw new Error(`Assert failed: Value is not a safe integer: ${t} (${typeof t})`);if(!(t>=0))throw new Error("Assert failed: Can't convert negative Number into UInt64");E$1(o$1.mono_wasm_f64_to_u52(e,t));}function A$1(e,t){if("bigint"!=typeof t)throw new Error(`Assert failed: Value is not an bigint: ${t} (${typeof t})`);if(!(t>=f$1&&t<=d$1))throw new Error(`Assert failed: Overflow: value ${t} is out of ${f$1} ${d$1} range`);Ke$1.HEAP64[e>>>3]=t;}function j$1(e,t){if("number"!=typeof t)throw new Error(`Assert failed: Value is not a Number: ${t} (${typeof t})`);Ke$1.HEAPF32[e>>>2]=t;}function $$1(e,t){if("number"!=typeof t)throw new Error(`Assert failed: Value is not a Number: ${t} (${typeof t})`);Ke$1.HEAPF64[e>>>3]=t;}let L$1=true;function R$1(e){const t=Ke$1.HEAPU32[e>>>2];return t>1&&L$1&&(L$1=false,We$1(`getB32: value at ${e} is not a boolean, but a number: ${t}`)),!!t}function B$1(e){return !!Ke$1.HEAPU8[e]}function N$1(e){return Ke$1.HEAPU8[e]}function O$1(e){return Ke$1.HEAPU16[e>>>1]}function C$1(e){return Ke$1.HEAPU32[e>>>2]}function D$1(e,t){return e[t>>>2]}function F$1(e){return o$1.mono_wasm_get_i32_unaligned(e)}function P$1(e){return o$1.mono_wasm_get_i32_unaligned(e)>>>0}function M$1(e){return Ke$1.HEAP8[e]}function z$1(e){return Ke$1.HEAP16[e>>>1]}function V$1(e){return Ke$1.HEAP32[e>>>2]}function W$1(e){const t=o$1.mono_wasm_i52_to_f64(e,ct$1._i52_error_scratch_buffer);return E$1(V$1(ct$1._i52_error_scratch_buffer)),t}function H$1(e){const t=o$1.mono_wasm_u52_to_f64(e,ct$1._i52_error_scratch_buffer);return E$1(V$1(ct$1._i52_error_scratch_buffer)),t}function q$1(e){return Ke$1.HEAP64[e>>>3]}function G$1(e){return Ke$1.HEAPF32[e>>>2]}function J$1(e){return Ke$1.HEAPF64[e>>>3]}function X$1(){return Ke$1.HEAP8}function Q$1(){return Ke$1.HEAP16}function Y$1(){return Ke$1.HEAP32}function Z$1(){return Ke$1.HEAP64}function K$1(){return Ke$1.HEAPU8}function ee$1(){return Ke$1.HEAPU16}function te$1(){return Ke$1.HEAPU32}function ne$1(){return Ke$1.HEAPF32}function re$1(){return Ke$1.HEAPF64}function oe$1(e,t){return e>>>t}let se$1=false;function ae$1(){if(se$1)throw new Error("GC is already locked");se$1=true;}function ie$1(){if(!se$1)throw new Error("GC is not locked");se$1=false;}const ce$1=8192;let le$1=null,pe$1=null,ue$1=0;const de$1=[],fe$1=[];function _e$1(e,t){if(e<=0)throw new Error("capacity >= 1");const n=4*(e|=0),r=_$1(n);if(r%4!=0)throw new Error("Malloc returned an unaligned offset");return g$1(r,n),new me$1(r,e,true,t)}let me$1 = class me{constructor(e,t,n,r){const s=4*t;this.__offset=e,this.__offset32=e>>>2,this.__count=t,this.length=t,this.__handle=o$1.mono_wasm_register_root(e,s,r||"noname"),this.__ownsAllocation=n;}_throw_index_out_of_range(){throw new Error("index out of range")}_check_in_range(e){(e>=this.__count||e<0)&&this._throw_index_out_of_range();}get_address(e){return this._check_in_range(e),this.__offset+4*e}get_address_32(e){return this._check_in_range(e),this.__offset32+e}get(e){this._check_in_range(e);const t=this.get_address_32(e);return te$1()[t]}set(e,t){const n=this.get_address(e);return o$1.mono_wasm_write_managed_pointer_unsafe(n,t),t}copy_value_from_address(e,t){const n=this.get_address(e);o$1.mono_wasm_copy_managed_pointer(n,t);}_unsafe_get(e){return te$1()[this.__offset32+e]}_unsafe_set(e,t){const n=this.__offset+e;o$1.mono_wasm_write_managed_pointer_unsafe(n,t);}clear(){this.__offset&&g$1(this.__offset,4*this.__count);}release(){this.__offset&&this.__ownsAllocation&&(o$1.mono_wasm_deregister_root(this.__offset),g$1(this.__offset,4*this.__count),m$1(this.__offset)),this.__handle=this.__offset=this.__count=this.__offset32=0;}toString(){return `[root buffer @${this.get_address(0)}, size ${this.__count} ]`}};let he$1 = class he{constructor(e,t){this.__buffer=e,this.__index=t;}get_address(){return this.__buffer.get_address(this.__index)}get_address_32(){return this.__buffer.get_address_32(this.__index)}get address(){return this.__buffer.get_address(this.__index)}get(){return this.__buffer._unsafe_get(this.__index)}set(e){const t=this.__buffer.get_address(this.__index);return o$1.mono_wasm_write_managed_pointer_unsafe(t,e),e}copy_from(e){const t=e.address,n=this.address;o$1.mono_wasm_copy_managed_pointer(n,t);}copy_to(e){const t=this.address,n=e.address;o$1.mono_wasm_copy_managed_pointer(n,t);}copy_from_address(e){const t=this.address;o$1.mono_wasm_copy_managed_pointer(t,e);}copy_to_address(e){const t=this.address;o$1.mono_wasm_copy_managed_pointer(e,t);}get value(){return this.get()}set value(e){this.set(e);}valueOf(){throw new Error("Implicit conversion of roots to pointers is no longer supported. Use .value or .address as appropriate")}clear(){const e=this.__buffer.get_address_32(this.__index);te$1()[e]=0;}release(){if(!this.__buffer)throw new Error("No buffer");var e;de$1.length>128?(void 0!==(e=this.__index)&&(le$1.set(e,0),pe$1[ue$1]=e,ue$1++),this.__buffer=null,this.__index=0):(this.set(0),de$1.push(this));}toString(){return `[root @${this.address}]`}};let ge$1 = class ge{constructor(e){this.__external_address=0,this.__external_address_32=0,this._set_address(e);}_set_address(e){this.__external_address=e,this.__external_address_32=e>>>2;}get address(){return this.__external_address}get_address(){return this.__external_address}get_address_32(){return this.__external_address_32}get(){return te$1()[this.__external_address_32]}set(e){return o$1.mono_wasm_write_managed_pointer_unsafe(this.__external_address,e),e}copy_from(e){const t=e.address,n=this.__external_address;o$1.mono_wasm_copy_managed_pointer(n,t);}copy_to(e){const t=this.__external_address,n=e.address;o$1.mono_wasm_copy_managed_pointer(n,t);}copy_from_address(e){const t=this.__external_address;o$1.mono_wasm_copy_managed_pointer(t,e);}copy_to_address(e){const t=this.__external_address;o$1.mono_wasm_copy_managed_pointer(e,t);}get value(){return this.get()}set value(e){this.set(e);}valueOf(){throw new Error("Implicit conversion of roots to pointers is no longer supported. Use .value or .address as appropriate")}clear(){te$1()[this.__external_address>>>2]=0;}release(){fe$1.length<128&&fe$1.push(this);}toString(){return `[external root @${this.address}]`}};const be$1=new Map,ye$1="";let we$1;const ke$1=new Map;let Se$1,ve$1,Ue$1,Te$1,Ee$1,xe$1=0,Ie$1=null,Ae$1=0;function je$1(e){if(void 0===Te$1){const t=Ke$1.lengthBytesUTF8(e),n=new Uint8Array(t);return Ke$1.stringToUTF8Array(e,n,0,t),n}return Te$1.encode(e)}function $e$1(e){const t=Ke$1.lengthBytesUTF8(e)+1,n=_$1(t),r=K$1().subarray(n,n+t);return Ke$1.stringToUTF8Array(e,r,0,t),r[t-1]=0,n}function Le$1(e){const t=K$1();return function(e,t,n){const r=t+n;let o=t;for(;e[o]&&!(o>=r);)++o;if(o-t<=16)return Ke$1.UTF8ArrayToString(e,t,n);if(void 0===Ue$1)return Ke$1.UTF8ArrayToString(e,t,n);const s=Fe$1(e,t,o);return Ue$1.decode(s)}(t,e,t.length-e)}function Re$1(e,t){if(Se$1){const n=Fe$1(K$1(),e,t);return Se$1.decode(n)}return function(e,t){let n="";const r=ee$1();for(let o=e;o<t;o+=2){const e=r[o>>>1];n+=String.fromCharCode(e);}return n}(e,t)}function Be$1(e,t,n){const r=ee$1(),o=n.length;for(let c=0;c<o&&(s=r,a=e,h$1(i=n.charCodeAt(c),0,65535),s[a>>>1]=i,!((e+=2)>=t));c++);var s,a,i;}function Ne$1(e){const t=2*(e.length+1),n=_$1(t);return g$1(n,2*e.length),Be$1(n,n+t,e),n}function Oe$1(e){if(e.value===p$1)return null;const t=we$1+0,n=we$1+4,r=we$1+8;let s;o$1.mono_wasm_string_get_data_ref(e.address,t,n,r);const a=te$1(),i=D$1(a,n),c=D$1(a,t),l=D$1(a,r);if(l&&(s=ke$1.get(e.value)),void 0===s&&(i&&c?(s=Re$1(c,c+i),l&&ke$1.set(e.value,s)):s=ye$1),void 0===s)throw new Error(`internal error when decoding string at location ${e.value}`);return s}function Ce$1(e,t){let n;if("symbol"==typeof e?(n=e.description,"string"!=typeof n&&(n=Symbol.keyFor(e)),"string"!=typeof n&&(n="<unknown Symbol>")):"string"==typeof e&&(n=e),"string"!=typeof n)throw new Error(`Argument to stringToInternedMonoStringRoot must be a string but was ${e}`);if(0===n.length&&xe$1)return void t.set(xe$1);const r=be$1.get(n);r?t.set(r):(De$1(n,t),function(e,t,n){if(!t.value)throw new Error("null pointer passed to _store_string_in_intern_table");Ae$1>=8192&&(Ie$1=null),Ie$1||(Ie$1=_e$1(8192,"interned strings"),Ae$1=0);const r=Ie$1,s=Ae$1++;if(o$1.mono_wasm_intern_string_ref(t.address),!t.value)throw new Error("mono_wasm_intern_string_ref produced a null pointer");be$1.set(e,t.value),ke$1.set(t.value,e),0!==e.length||xe$1||(xe$1=t.value),r.copy_value_from_address(s,t.address);}(n,t));}function De$1(e,t){const n=2*(e.length+1),r=_$1(n);Be$1(r,r+n,e),o$1.mono_wasm_string_from_utf16_ref(r,e.length,t.address),m$1(r);}function Fe$1(e,t,n){return e.buffer,e.subarray(t,n)}function Pe$1(e){if(e===p$1)return null;Ee$1.value=e;const t=Oe$1(Ee$1);return Ee$1.value=p$1,t}let Me$1="MONO_WASM: ";function ze$1(e){if(ct$1.diagnosticTracing){const t="function"==typeof e?e():e;console.debug(Me$1+t);}}function Ve$1(e,...t){console.info(Me$1+e,...t);}function We$1(e,...t){console.warn(Me$1+e,...t);}function He$1(e,...t){if(t&&t.length>0&&t[0]&&"object"==typeof t[0]){if(t[0].silent)return;if(t[0].toString)return void console.error(Me$1+e,t[0].toString())}console.error(Me$1+e,...t);}const qe$1=new Map;let Ge$1;const Je$1=[];function Xe$1(e){try{if(Ye$1(),0==qe$1.size)return e;const t=e;for(let n=0;n<Je$1.length;n++){const r=e.replace(new RegExp(Je$1[n],"g"),((e,...t)=>{const n=t.find((e=>"object"==typeof e&&void 0!==e.replaceSection));if(void 0===n)return e;const r=n.funcNum,o=n.replaceSection,s=qe$1.get(Number(r));return void 0===s?e:e.replace(o,`${s} (${o})`)}));if(r!==t)return r}return t}catch(t){return console.debug(`failed to symbolicate: ${t}`),e}}function Qe$1(e){let t;return t="string"==typeof e?e:null==e||void 0===e.stack?(new Error).stack+"":e.stack+"",Xe$1(t)}function Ye$1(){if(!Ge$1)return;Je$1.push(/at (?<replaceSection>[^:()]+:wasm-function\[(?<funcNum>\d+)\]:0x[a-fA-F\d]+)((?![^)a-fA-F\d])|$)/),Je$1.push(/(?:WASM \[[\da-zA-Z]+\], (?<replaceSection>function #(?<funcNum>[\d]+) \(''\)))/),Je$1.push(/(?<replaceSection>[a-z]+:\/\/[^ )]*:wasm-function\[(?<funcNum>\d+)\]:0x[a-fA-F\d]+)/),Je$1.push(/(?<replaceSection><[^ >]+>[.:]wasm-function\[(?<funcNum>[0-9]+)\])/);const e=Ge$1;Ge$1=void 0;try{e.split(/[\r\n]/).forEach((e=>{const t=e.split(/:/);t.length<2||(t[1]=t.splice(1).join(":"),qe$1.set(Number(t[0]),t[1]));})),lt$1.diagnosticTracing&&ze$1(`Loaded ${qe$1.size} symbols`);}catch(e){We$1(`Failed to load symbol map: ${e}`);}}function Ze$1(){return Ye$1(),[...qe$1.values()]}let Ke$1,et$1;const tt$1="object"==typeof process&&"object"==typeof process.versions&&"string"==typeof process.versions.node,nt$1="function"==typeof importScripts,rt$1=nt$1&&"undefined"!=typeof dotnetSidecar,ot$1=nt$1&&!rt$1,st$1="object"==typeof window||nt$1&&!tt$1,at$1=!st$1&&!tt$1;let it$1=null,ct$1=null,lt$1=null,pt=null,ut$1=false;function dt$1(e,t){ct$1.emscriptenBuildOptions=t,e.isPThread,ct$1.quit=e.quit_,ct$1.ExitStatus=e.ExitStatus,ct$1.getMemory=e.getMemory,ct$1.getWasmIndirectFunctionTable=e.getWasmIndirectFunctionTable,ct$1.updateMemoryViews=e.updateMemoryViews;}function ft$1(e){if(ut$1)throw new Error("Runtime module already loaded");ut$1=true,Ke$1=e.module,et$1=e.internal,ct$1=e.runtimeHelpers,lt$1=e.loaderHelpers,pt=e.diagnosticHelpers,it$1=e.api;const t={gitHash:"e2f47b0110ed922f21a1522da67279133ce28f32",coreAssetsInMemory:_t(),allAssetsInMemory:_t(),dotnetReady:_t(),afterInstantiateWasm:_t(),beforePreInit:_t(),afterPreInit:_t(),afterPreRun:_t(),beforeOnRuntimeInitialized:_t(),afterMonoStarted:_t(),afterDeputyReady:_t(),afterIOStarted:_t(),afterOnRuntimeInitialized:_t(),afterPostRun:_t(),nativeAbort:e=>{throw e||new Error("abort")},nativeExit:e=>{throw new Error("exit:"+e)}};Object.assign(ct$1,t),Object.assign(e.module.config,{}),Object.assign(e.api,{Module:e.module,...e.module}),Object.assign(e.api,{INTERNAL:e.internal});}function _t(e,t){return lt$1.createPromiseController(e,t)}function mt(e,t){if(e)return;const n="Assert failed: "+("function"==typeof t?t():t),r=new Error(n);He$1(n,r),ct$1.nativeAbort(r);}function ht(e,t,n){const r=function(e,t,n){let r,o=0;r=e.length-o;const s={read:function(){if(o>=r)return null;const t=e[o];return o+=1,t}};return Object.defineProperty(s,"eof",{get:function(){return o>=r},configurable:true,enumerable:true}),s}(e);let o="",s=0,a=0,i=0,c=0,l=0,p=0;for(;s=r.read(),a=r.read(),i=r.read(),null!==s;)null===a&&(a=0,l+=1),null===i&&(i=0,l+=1),p=s<<16|a<<8|i,c=(16777215&p)>>18,o+=gt[c],c=(262143&p)>>12,o+=gt[c],l<2&&(c=(4095&p)>>6,o+=gt[c]),2===l?o+="==":1===l?o+="=":(c=63&p,o+=gt[c]);return o}const gt=["A","B","C","D","E","F","G","H","I","J","K","L","M","N","O","P","Q","R","S","T","U","V","W","X","Y","Z","a","b","c","d","e","f","g","h","i","j","k","l","m","n","o","p","q","r","s","t","u","v","w","x","y","z","0","1","2","3","4","5","6","7","8","9","+","/"],bt=new Map;bt.remove=function(e){const t=this.get(e);return this.delete(e),t};let yt,wt,kt,St={},vt=0,Ut=-1;function mono_wasm_fire_debugger_agent_message_with_data_to_pause(e){console.assert(true,`mono_wasm_fire_debugger_agent_message_with_data ${e}`);debugger}function Tt(e){e.length>Ut&&(yt&&m$1(yt),Ut=Math.max(e.length,Ut,256),yt=_$1(Ut));const t=atob(e),n=K$1();for(let e=0;e<t.length;e++)n[yt+e]=t.charCodeAt(e);}function Et(e,t,n,r,s,a,i){Tt(r),o$1.mono_wasm_send_dbg_command_with_parms(e,t,n,yt,s,a,i.toString());const{res_ok:c,res:l}=bt.remove(e);if(!c)throw new Error("Failed on mono_wasm_send_dbg_command_with_parms");return l}function xt(e,t,n,r){Tt(r),o$1.mono_wasm_send_dbg_command(e,t,n,yt,r.length);const{res_ok:s,res:a}=bt.remove(e);if(!s)throw new Error("Failed on mono_wasm_send_dbg_command");return a}function It(){const{res_ok:e,res:t}=bt.remove(0);if(!e)throw new Error("Failed on mono_wasm_get_dbg_command_info");return t}function At(){}function jt(){o$1.mono_wasm_set_is_debugger_attached(false);}function $t(e){o$1.mono_wasm_change_debugger_log_level(e);}function Lt(e,t={}){if("object"!=typeof e)throw new Error(`event must be an object, but got ${JSON.stringify(e)}`);if(void 0===e.eventName)throw new Error(`event.eventName is a required parameter, in event: ${JSON.stringify(e)}`);if("object"!=typeof t)throw new Error(`args must be an object, but got ${JSON.stringify(t)}`);console.debug("mono_wasm_debug_event_raised:aef14bca-5519-4dfe-b35a-f867abc123ae",JSON.stringify(e),JSON.stringify(t));}function Rt(){ -1==ct$1.waitForDebugger&&(ct$1.waitForDebugger=1),o$1.mono_wasm_set_is_debugger_attached(true);}function Bt(e){if(null!=e.arguments&&!Array.isArray(e.arguments))throw new Error(`"arguments" should be an array, but was ${e.arguments}`);const t=e.objectId,n=e.details;let r={};if(t.startsWith("dotnet:cfo_res:")){if(!(t in St))throw new Error(`Unknown object id ${t}`);r=St[t];}else r=function(e,t){if(e.startsWith("dotnet:array:")){let e;if(void 0===t.items)return e=t.map((e=>e.value)),e;if(void 0===t.dimensionsDetails||1===t.dimensionsDetails.length)return e=t.items.map((e=>e.value)),e}const n={};return Object.keys(t).forEach((e=>{const r=t[e];void 0!==r.get?Object.defineProperty(n,r.name,{get:()=>xt(r.get.id,r.get.commandSet,r.get.command,r.get.buffer),set:function(e){return Et(r.set.id,r.set.commandSet,r.set.command,r.set.buffer,r.set.length,r.set.valtype,e),true}}):void 0!==r.set?Object.defineProperty(n,r.name,{get:()=>r.value,set:function(e){return Et(r.set.id,r.set.commandSet,r.set.command,r.set.buffer,r.set.length,r.set.valtype,e),true}}):n[r.name]=r.value;})),n}(t,n);const o=null!=e.arguments?e.arguments.map((e=>JSON.stringify(e.value))):[],s=`const fn = ${e.functionDeclaration}; return fn.apply(proxy, [${o}]);`,a=new Function("proxy",s)(r);if(void 0===a)return {type:"undefined"};if(Object(a)!==a)return "object"==typeof a&&null==a?{type:typeof a,subtype:`${a}`,value:null}:{type:typeof a,description:`${a}`,value:`${a}`};if(e.returnByValue&&null==a.subtype)return {type:"object",value:a};if(Object.getPrototypeOf(a)==Array.prototype){const e=Ot(a);return {type:"object",subtype:"array",className:"Array",description:`Array(${a.length})`,objectId:e}}return void 0!==a.value||void 0!==a.subtype?a:a==r?{type:"object",className:"Object",description:"Object",objectId:t}:{type:"object",className:"Object",description:"Object",objectId:Ot(a)}}function Nt(e,t={}){return function(e,t){if(!(e in St))throw new Error(`Could not find any object with id ${e}`);const n=St[e],r=Object.getOwnPropertyDescriptors(n);t.accessorPropertiesOnly&&Object.keys(r).forEach((e=>{ void 0===r[e].get&&Reflect.deleteProperty(r,e);}));const o=[];return Object.keys(r).forEach((e=>{let t;const n=r[e];t="object"==typeof n.value?Object.assign({name:e},n):void 0!==n.value?{name:e,value:Object.assign({type:typeof n.value,description:""+n.value},n)}:void 0!==n.get?{name:e,get:{className:"Function",description:`get ${e} () {}`,type:"function"}}:{name:e,value:{type:"symbol",value:"<Unknown>",description:"<Unknown>"}},o.push(t);})),{__value_as_json_string__:JSON.stringify(o)}}(`dotnet:cfo_res:${e}`,t)}function Ot(e){const t="dotnet:cfo_res:"+vt++;return St[t]=e,t}function Ct(e){e in St&&delete St[e];}let Dt=false;function Ft(){if(Dt)return globalThis.performance.now()}function Pt(e,t,n){if(Dt&&e){const r=st$1?{start:e}:{startTime:e},o=n?`${t}${n} `:t;globalThis.performance.measure(o,r);}}const Mt=new Map;function zt(e,t,n){if(0===t||1===t||2===t||26===t)return;let r,o,s,a;o=to(Cn(e)),s=to(Dn(e)),a=to(Fn(e));const i=On(e);r=Vt(i),19===t&&(t=i);const c=Vt(t),l=Cn(e),p=n*In;return e=>c(e+p,l,r,o,s,a)}function Vt(e){if(0===e||1===e)return;const t=vn.get(e);return t&&"function"==typeof t||mt(false,`ERR41: Unknown converter for type ${e}. ${Kr}`),t}function Wt(e){return 0==zn(e)?null:function(e){return e||mt(false,"Null arg"),B$1(e)}(e)}function Ht(e){return 0==zn(e)?null:function(e){return e||mt(false,"Null arg"),N$1(e)}(e)}function qt(e){return 0==zn(e)?null:function(e){return e||mt(false,"Null arg"),O$1(e)}(e)}function Gt(e){return 0==zn(e)?null:function(e){return e||mt(false,"Null arg"),z$1(e)}(e)}function Jt(e){return 0==zn(e)?null:function(e){return e||mt(false,"Null arg"),V$1(e)}(e)}function Xt(e){return 0==zn(e)?null:function(e){return e||mt(false,"Null arg"),J$1(e)}(e)}function Qt(e){return 0==zn(e)?null:function(e){return e||mt(false,"Null arg"),q$1(e)}(e)}function Yt(e){return 0==zn(e)?null:function(e){return e||mt(false,"Null arg"),G$1(e)}(e)}function Zt(e){return 0==zn(e)?null:function(e){return e||mt(false,"Null arg"),J$1(e)}(e)}function Kt(e){return 0==zn(e)?null:Hn(e)}function en(){return null}function tn(e){return 0===zn(e)?null:function(e){e||mt(false,"Null arg");const t=J$1(e);return new Date(t)}(e)}function nn(e,t,n,r,o,s){if(0===zn(e))return null;const a=Zn(e);let i=qr(a);return null==i&&(i=(e,t,i)=>function(e,t,n,r,o,s,a,i){lt$1.assert_runtime_running();const c=Ke$1.stackSave();try{const c=$n(6),l=Ln(c,2);if(Wn(l,14),Kn(l,e),s&&s(Ln(c,3),t),a&&a(Ln(c,4),n),i&&i(Ln(c,5),r),kn(yn.CallDelegate,c),o)return o(Ln(c,1))}finally{lt$1.is_runtime_running()&&Ke$1.stackRestore(c);}}(a,e,t,i,n,r,o,s),i.dispose=()=>{i.isDisposed||(i.isDisposed=true,Vr(i,a));},i.isDisposed=false,zr(i,a)),i}class rn{constructor(e,t){this.promise=e,this.resolve_or_reject=t;}}function on(e,t,n){const r=zn(e);30==r&&mt(false,"Unexpected Task type: TaskPreCreated");const o=cn(e,r,n);if(false!==o)return o;const s=Qn(e),a=ln(n);return function(e,t){hr(),xr[0-t]=e,Object.isExtensible(e)&&(e[Cr]=t);}(a,s),a.promise}function sn(e,t,n){const r=ln(n);return Yn(e,Pr(r)),Wn(e,30),r.promise}function an(e,t,n){const r=Ln(e,1),o=zn(r);if(30===o)return n;Mr(Pr(n));const s=cn(r,o,t);return  false===s&&mt(false,`Expected synchronous result, got: ${o}`),s}function cn(e,t,n){if(0===t)return null;if(29===t)return Promise.reject(un(e));if(28===t){const t=Vn(e);if(1===t)return Promise.resolve();Wn(e,t),n||(n=vn.get(t)),n||mt(false,`Unknown sub_converter for type ${t}. ${Kr}`);const r=n(e);return Promise.resolve(r)}return  false}function ln(e){const{promise:t,promise_control:n}=lt$1.createPromiseController();return new rn(t,((t,r,o)=>{if(29===t){const e=un(o);n.reject(e);}else if(28===t){const t=zn(o);if(1===t)n.resolve(void 0);else {e||(e=vn.get(t)),e||mt(false,`Unknown sub_converter for type ${t}. ${Kr}`);const r=e(o);n.resolve(r);}}else mt(false,`Unexpected type ${t}`);Mr(r);}))}function pn(e){if(0==zn(e))return null;{const t=er(e);try{return Oe$1(t)}finally{t.release();}}}function un(e){const t=zn(e);if(0==t)return null;if(27==t)return Fr(Qn(e));const n=Zn(e);let r=qr(n);if(null==r){const t=pn(e);r=new ManagedError(t),zr(r,n);}return r}function dn(e){if(0==zn(e))return null;const t=Qn(e),n=Fr(t);return void 0===n&&mt(false,`JS object JSHandle ${t} was not found`),n}function fn(e){const t=zn(e);if(0==t)return null;if(13==t)return Fr(Qn(e));if(21==t)return mn(e,Vn(e));if(14==t){const t=Zn(e);if(t===u$1)return null;let n=qr(t);return n||(n=new ManagedObject,zr(n,t)),n}const n=vn.get(t);return n||mt(false,`Unknown converter for type ${t}. ${Kr}`),n(e)}function _n(e,t){return t||mt(false,"Expected valid element_type parameter"),mn(e,t)}function mn(e,t){if(0==zn(e))return null;-1==rr(t)&&mt(false,`Element type ${t} not supported`);const n=Hn(e),r=tr(e);let s=null;if(15==t){s=new Array(r);for(let e=0;e<r;e++){const t=Ln(n,e);s[e]=pn(t);}o$1.mono_wasm_deregister_root(n);}else if(14==t){s=new Array(r);for(let e=0;e<r;e++){const t=Ln(n,e);s[e]=fn(t);}o$1.mono_wasm_deregister_root(n);}else if(13==t){s=new Array(r);for(let e=0;e<r;e++){const t=Ln(n,e);s[e]=dn(t);}}else if(4==t){const e=oe$1(n,0);s=K$1().subarray(e,e+r).slice();}else if(7==t){const e=oe$1(n,2);s=Y$1().subarray(e,e+r).slice();}else {if(10!=t)throw new Error(`NotImplementedException ${t}. ${Kr}`);{const e=oe$1(n,3);s=re$1().subarray(e,e+r).slice();}}return m$1(n),s}function hn(e,t){t||mt(false,"Expected valid element_type parameter");const n=Hn(e),r=tr(e);let o=null;if(4==t)o=new Span(n,r,0);else if(7==t)o=new Span(n,r,1);else {if(10!=t)throw new Error(`NotImplementedException ${t}. ${Kr}`);o=new Span(n,r,2);}return o}function gn(e,t){t||mt(false,"Expected valid element_type parameter");const n=Hn(e),r=tr(e);let o=null;if(4==t)o=new ArraySegment(n,r,0);else if(7==t)o=new ArraySegment(n,r,1);else {if(10!=t)throw new Error(`NotImplementedException ${t}. ${Kr}`);o=new ArraySegment(n,r,2);}return zr(o,Zn(e)),o}const bn={pthreadId:0,reuseCount:0,updateCount:0,threadPrefix:"          -    ",threadName:"emscripten-loaded"},yn={};function wn(e,t,n,r){if(hr(),o$1.mono_wasm_invoke_jsexport(t,n),Rn(n))throw un(Ln(n,0))}function kn(e,t){if(hr(),o$1.mono_wasm_invoke_jsexport(e,t),Rn(t))throw un(Ln(t,0))}function Sn(e){const t=o$1.mono_wasm_assembly_find_method(ct$1.runtime_interop_exports_class,e,-1);if(!t)throw "Can't find method "+ct$1.runtime_interop_namespace+"."+ct$1.runtime_interop_exports_classname+"."+e;return t}const vn=new Map,Un=new Map,Tn=Symbol.for("wasm bound_cs_function"),En=Symbol.for("wasm bound_js_function"),xn=Symbol.for("wasm imported_js_function"),In=32,An=32,jn=32;function $n(e){const t=In*e,n=Ke$1.stackAlloc(t);return g$1(n,t),n}function Ln(e,t){return e||mt(false,"Null args"),e+t*In}function Rn(e){return e||mt(false,"Null args"),0!==zn(e)}function Bn(e,t){return e||mt(false,"Null signatures"),e+t*An+jn}function Nn(e){return e||mt(false,"Null sig"),N$1(e+0)}function On(e){return e||mt(false,"Null sig"),N$1(e+16)}function Cn(e){return e||mt(false,"Null sig"),N$1(e+20)}function Dn(e){return e||mt(false,"Null sig"),N$1(e+24)}function Fn(e){return e||mt(false,"Null sig"),N$1(e+28)}function Pn(e){return e||mt(false,"Null signatures"),V$1(e+4)}function Mn(e){return e||mt(false,"Null signatures"),V$1(e+0)}function zn(e){return e||mt(false,"Null arg"),N$1(e+12)}function Vn(e){return e||mt(false,"Null arg"),N$1(e+13)}function Wn(e,t){e||mt(false,"Null arg"),w$1(e+12,t);}function Hn(e){return e||mt(false,"Null arg"),C$1(e)}function qn(e,t){if(e||mt(false,"Null arg"),"boolean"!=typeof t)throw new Error(`Assert failed: Value is not a Boolean: ${t} (${typeof t})`);y$1(e,t);}function Gn(e,t){e||mt(false,"Null arg"),S$1(e,t);}function Jn(e,t){e||mt(false,"Null arg"),$$1(e,t.getTime());}function Xn(e,t){e||mt(false,"Null arg"),$$1(e,t);}function Qn(e){return e||mt(false,"Null arg"),V$1(e+4)}function Yn(e,t){e||mt(false,"Null arg"),T$1(e+4,t);}function Zn(e){return e||mt(false,"Null arg"),V$1(e+4)}function Kn(e,t){e||mt(false,"Null arg"),T$1(e+4,t);}function er(e){return e||mt(false,"Null arg"),function(e){let t;if(!e)throw new Error("address must be a location in the native heap");return fe$1.length>0?(t=fe$1.pop(),t._set_address(e)):t=new ge$1(e),t}(e)}function tr(e){return e||mt(false,"Null arg"),V$1(e+8)}function nr(e,t){e||mt(false,"Null arg"),T$1(e+8,t);}class ManagedObject{dispose(){Vr(this,u$1);}get isDisposed(){return this[Or]===u$1}toString(){return `CsObject(gc_handle: ${this[Or]})`}}class ManagedError extends Error{constructor(e){super(e),this.superStack=Object.getOwnPropertyDescriptor(this,"stack"),Object.defineProperty(this,"stack",{get:this.getManageStack});}getSuperStack(){if(this.superStack){if(void 0!==this.superStack.value)return this.superStack.value;if(void 0!==this.superStack.get)return this.superStack.get.call(this)}return super.stack}getManageStack(){if(this.managed_stack)return this.managed_stack;if(!lt$1.is_runtime_running())return this.managed_stack="... omitted managed stack trace.\n"+this.getSuperStack(),this.managed_stack;{const e=this[Or];if(e!==u$1){const t=function(e){lt$1.assert_runtime_running();const t=Ke$1.stackSave();try{const t=$n(3),n=Ln(t,2);return Wn(n,16),Kn(n,e),kn(yn.GetManagedStackTrace,t),pn(Ln(t,1))}finally{lt$1.is_runtime_running()&&Ke$1.stackRestore(t);}}(e);if(t)return this.managed_stack=t+"\n"+this.getSuperStack(),this.managed_stack}}return this.getSuperStack()}dispose(){Vr(this,u$1);}get isDisposed(){return this[Or]===u$1}}function rr(e){return 4==e?1:7==e?4:8==e||10==e?8:15==e||14==e||13==e?In:-1}class or{constructor(e,t,n){this._pointer=e,this._length=t,this._viewType=n;}_unsafe_create_view(){const e=0==this._viewType?new Uint8Array(K$1().buffer,this._pointer,this._length):1==this._viewType?new Int32Array(Y$1().buffer,this._pointer,this._length):2==this._viewType?new Float64Array(re$1().buffer,this._pointer,this._length):null;if(!e)throw new Error("NotImplementedException");return e}set(e,t){if(this.isDisposed)throw new Error("Assert failed: ObjectDisposedException");const n=this._unsafe_create_view();if(!e||!n||e.constructor!==n.constructor)throw new Error(`Assert failed: Expected ${n.constructor}`);n.set(e,t);}copyTo(e,t){if(this.isDisposed)throw new Error("Assert failed: ObjectDisposedException");const n=this._unsafe_create_view();if(!e||!n||e.constructor!==n.constructor)throw new Error(`Assert failed: Expected ${n.constructor}`);const r=n.subarray(t);e.set(r);}slice(e,t){if(this.isDisposed)throw new Error("Assert failed: ObjectDisposedException");return this._unsafe_create_view().slice(e,t)}get length(){if(this.isDisposed)throw new Error("Assert failed: ObjectDisposedException");return this._length}get byteLength(){if(this.isDisposed)throw new Error("Assert failed: ObjectDisposedException");return 0==this._viewType?this._length:1==this._viewType?this._length<<2:2==this._viewType?this._length<<3:0}}class Span extends or{constructor(e,t,n){super(e,t,n),this.is_disposed=false;}dispose(){this.is_disposed=true;}get isDisposed(){return this.is_disposed}}class ArraySegment extends or{constructor(e,t,n){super(e,t,n);}dispose(){Vr(this,u$1);}get isDisposed(){return this[Or]===u$1}}const sr=[null];function ar(e){const t=e.args_count,r=e.arg_marshalers,o=e.res_converter,s=e.arg_cleanup,a=e.has_cleanup,i=e.fn,c=e.fqn;return e=null,function(l){const p=Ft();try{n$1&&e.isDisposed;const c=new Array(t);for(let e=0;e<t;e++){const t=(0,r[e])(l);c[e]=t;}const p=i(...c);if(o&&o(l,p),a)for(let e=0;e<t;e++){const t=s[e];t&&t(c[e]);}}catch(e){wo(l,e);}finally{Pt(p,"mono.callCsFunction:",c);}}}function ir(e,t){_r.set(e,t),lt$1.diagnosticTracing&&ze$1(`added module imports '${e}'`);}function cr(e,t,n){if(!e)throw new Error("Assert failed: Null reference");e[t]=n;}function lr(e,t){if(!e)throw new Error("Assert failed: Null reference");return e[t]}function pr(e,t){if(!e)throw new Error("Assert failed: Null reference");return t in e}function ur(e,t){if(!e)throw new Error("Assert failed: Null reference");return typeof e[t]}function dr(){return globalThis}const fr=new Map,_r=new Map;function mr(e,t){hr(),e&&"string"==typeof e||mt(false,"module_name must be string"),t&&"string"==typeof t||mt(false,"module_url must be string");let n=fr.get(e);const r=!n;return r&&(lt$1.diagnosticTracing&&ze$1(`importing ES6 module '${e}' from '${t}'`),n=import(((s) => typeof s === "string" && s[0] === "." && (s[1] === "/" || (s[1] === "." && s[2] === "/")) ? new URL(s, new URL("_framework/", __spawnjsBundleUrl).href).href : s)(/*! webpackIgnore: true */t)),fr.set(e,n)),Qr((async()=>{const o=await n;return r&&(_r.set(e,o),lt$1.diagnosticTracing&&ze$1(`imported ES6 module '${e}' from '${t}'`)),o}))}function hr(){lt$1.assert_runtime_running(),ct$1.mono_wasm_bindings_is_ready||mt(false,"The runtime must be initialized.");}function gr(e){e();}const br="function"==typeof globalThis.WeakRef;function yr(e){return br?new WeakRef(e):function(e){return {deref:()=>e,dispose:()=>{e=null;}}}(e)}function wr(e,t,n,r,o,s,a){const i=`[${t}] ${n}.${r}:${o}`,c=Ft();lt$1.diagnosticTracing&&ze$1(`Binding [JSExport] ${n}.${r}:${o} from ${t} assembly`);const l=Mn(a);2!==l&&mt(false,`Signature version ${l} mismatch.`);const p=Pn(a),u=new Array(p);for(let e=0;e<p;e++){const t=Bn(a,e+2),n=eo(t,Nn(t),e+2);n||mt(false,"ERR43: argument marshaler must be resolved"),u[e]=n;}const d=Bn(a,1);let f=Nn(d);const _=20==f,m=26==f;_&&(f=30);const h=zt(d,f,1),g={method:e,fullyQualifiedName:i,args_count:p,arg_marshalers:u,res_converter:h,is_async:_,is_discard_no_wait:m,isDisposed:false};let b;b=_?1==p&&h?function(e){const t=e.method,n=e.arg_marshalers[0],r=e.res_converter,o=e.fullyQualifiedName;return e=null,function(e){const s=Ft();lt$1.assert_runtime_running();const a=Ke$1.stackSave();try{const o=$n(3);n(o,e);let s=r(o);return wn(ct$1.managedThreadTID,t,o),s=an(o,void 0,s),s}finally{lt$1.is_runtime_running()&&Ke$1.stackRestore(a),Pt(s,"mono.callCsFunction:",o);}}}(g):2==p&&h?function(e){const t=e.method,n=e.arg_marshalers[0],r=e.arg_marshalers[1],o=e.res_converter,s=e.fullyQualifiedName;return e=null,function(e,a){const i=Ft();lt$1.assert_runtime_running();const c=Ke$1.stackSave();try{const s=$n(4);n(s,e),r(s,a);let i=o(s);return wn(ct$1.managedThreadTID,t,s),i=an(s,void 0,i),i}finally{lt$1.is_runtime_running()&&Ke$1.stackRestore(c),Pt(i,"mono.callCsFunction:",s);}}}(g):kr(g):m?kr(g):0!=p||h?1!=p||h?1==p&&h?function(e){const t=e.method,n=e.arg_marshalers[0],r=e.res_converter,o=e.fullyQualifiedName;return e=null,function(e){const s=Ft();lt$1.assert_runtime_running();const a=Ke$1.stackSave();try{const o=$n(3);return n(o,e),kn(t,o),r(o)}finally{lt$1.is_runtime_running()&&Ke$1.stackRestore(a),Pt(s,"mono.callCsFunction:",o);}}}(g):2==p&&h?function(e){const t=e.method,n=e.arg_marshalers[0],r=e.arg_marshalers[1],o=e.res_converter,s=e.fullyQualifiedName;return e=null,function(e,a){const i=Ft();lt$1.assert_runtime_running();const c=Ke$1.stackSave();try{const s=$n(4);return n(s,e),r(s,a),kn(t,s),o(s)}finally{lt$1.is_runtime_running()&&Ke$1.stackRestore(c),Pt(i,"mono.callCsFunction:",s);}}}(g):kr(g):function(e){const t=e.method,n=e.arg_marshalers[0],r=e.fullyQualifiedName;return e=null,function(e){const o=Ft();lt$1.assert_runtime_running();const s=Ke$1.stackSave();try{const r=$n(3);n(r,e),kn(t,r);}finally{lt$1.is_runtime_running()&&Ke$1.stackRestore(s),Pt(o,"mono.callCsFunction:",r);}}}(g):function(e){const t=e.method,n=e.fullyQualifiedName;return e=null,function(){const e=Ft();lt$1.assert_runtime_running();const r=Ke$1.stackSave();try{const e=$n(2);kn(t,e);}finally{lt$1.is_runtime_running()&&Ke$1.stackRestore(r),Pt(e,"mono.callCsFunction:",n);}}}(g),b[Tn]=g,function(e,t,n,r,o,s){const a=`${t}.${n}`.replace(/\//g,".").split(".");let i,c=Sr.get(e);c||(c={},Sr.set(e,c),Sr.set(e+".dll",c)),i=c;for(let e=0;e<a.length;e++){const t=a[e];if(""!=t){let e=i[t];void 0===e&&(e={},i[t]=e),e||mt(false,`${t} not found while looking up ${n}`),i=e;}}i[r]||(i[r]=s),i[`${r}.${o}`]=s;}(t,n,r,o,s,b),Pt(c,"mono.bindCsFunction:",i);}function kr(e){const t=e.args_count,n=e.arg_marshalers,r=e.res_converter,o=e.method,s=e.fullyQualifiedName,a=e.is_async,i=e.is_discard_no_wait;return e=null,function(...e){const c=Ft();lt$1.assert_runtime_running();const l=Ke$1.stackSave();try{const s=$n(2+t);for(let r=0;r<t;r++){const t=n[r];t&&t(s,e[r]);}let c;return a&&(c=r(s)),a?(wn(ct$1.managedThreadTID,o,s),c=an(s,void 0,c)):i?wn(ct$1.managedThreadTID,o,s):(kn(o,s),r&&(c=r(s))),c}finally{lt$1.is_runtime_running()&&Ke$1.stackRestore(l),Pt(c,"mono.callCsFunction:",s);}}}const Sr=new Map;async function vr(e){return hr(),Sr.get(e)||await function(e){lt$1.assert_runtime_running();const t=Ke$1.stackSave();try{const t=$n(3),n=Ln(t,1);mo(Ln(t,2),e);let r=sn(n);return wn(ct$1.managedThreadTID,yn.BindAssemblyExports,t),r=an(t,Jt,r),null==r&&(r=Promise.resolve()),r}finally{lt$1.is_runtime_running()&&Ke$1.stackRestore(t);}}(e),Sr.get(e)||{}}const Ur="function"==typeof globalThis.FinalizationRegistry;let Tr;const Er=[null],xr=[null],Ir=[];let Ar=1;const jr=new Map,$r=[];let Lr=-2;function Rr(e){return e<-1}function Br(e){return e>0}function Nr(e){return e<-1}Ur&&(Tr=new globalThis.FinalizationRegistry(Hr));const Or=Symbol.for("wasm js_owned_gc_handle"),Cr=Symbol.for("wasm cs_owned_js_handle"),Dr=Symbol.for("wasm do_not_force_dispose");function Fr(e){return Br(e)?Er[e]:Rr(e)?xr[0-e]:null}function Pr(e){if(hr(),e[Cr])return e[Cr];const t=Ir.length?Ir.pop():Ar++;return Er[t]=e,Object.isExtensible(e)&&("function"==typeof e&&Object.prototype.hasOwnProperty.call(e,"prototype")||(e[Cr]=t)),t}function Mr(e){let t;Br(e)?(t=Er[e],Er[e]=void 0,Ir.push(e)):Rr(e)&&(t=xr[0-e],xr[0-e]=void 0),null==t&&mt(false,"ObjectDisposedException"),void 0!==t[Cr]&&(t[Cr]=void 0);}function zr(e,t){hr(),e[Or]=t,Ur&&Tr.register(e,t,e);const n=yr(e);jr.set(t,n);}function Vr(e,t,r){var o;hr(),e&&(t=e[Or],e[Or]=u$1,Ur&&Tr.unregister(e)),t!==u$1&&jr.delete(t)&&!r&&lt$1.is_runtime_running()&&!Gr&&function(e){e||mt(false,"Must be valid gc_handle"),lt$1.assert_runtime_running();const t=Ke$1.stackSave();try{const t=$n(3),r=Ln(t,2);Wn(r,14),Kn(r,e),n$1&&!Nr(e)&&bn.isUI||kn(yn.ReleaseJSOwnedObjectByGCHandle,t);}finally{lt$1.is_runtime_running()&&Ke$1.stackRestore(t);}}(t),Nr(t)&&(o=t,$r.push(o));}function Wr(e){const t=e[Or];if(t==u$1)throw new Error("Assert failed: ObjectDisposedException");return t}function Hr(e){lt$1.is_runtime_running()&&Vr(null,e);}function qr(e){if(!e)return null;const t=jr.get(e);return t?t.deref():null}let Gr=false;function Jr(e,t){let n=false,r=false;Gr=true;let o=0,s=0,a=0,i=0;const c=[...jr.keys()];for(const e of c){const r=jr.get(e),o=r&&r.deref();if(Ur&&o&&Tr.unregister(o),o){const s="boolean"==typeof o[Dr]&&o[Dr];if(t&&We$1(`Proxy of C# ${typeof o} with GCHandle ${e} was still alive. ${s?"keeping":"disposing"}.`),s)n=true;else {const t=lt$1.getPromiseController(o);t&&t.reject(new Error("WebWorker which is origin of the Task is being terminated.")),"function"==typeof o.dispose&&o.dispose(),o[Or]===e&&(o[Or]=u$1),!br&&r&&r.dispose(),a++;}}}n||(jr.clear(),Ur&&(Tr=new globalThis.FinalizationRegistry(Hr)));const l=(e,n)=>{const o=n[e],s=o&&"boolean"==typeof o[Dr]&&o[Dr];if(s||(n[e]=void 0),o)if(t&&We$1(`Proxy of JS ${typeof o} with JSHandle ${e} was still alive. ${s?"keeping":"disposing"}.`),s)r=true;else {const t=lt$1.getPromiseController(o);t&&t.reject(new Error("WebWorker which is origin of the Task is being terminated.")),"function"==typeof o.dispose&&o.dispose(),o[Cr]===e&&(o[Cr]=void 0),i++;}};for(let e=0;e<Er.length;e++)l(e,Er);for(let e=0;e<xr.length;e++)l(e,xr);if(r||(Er.length=1,xr.length=1,Ar=1,Ir.length=0),$r.length=0,Lr=-2,e){for(const e of sr)if(e){const t=e[xn];t&&(t.disposed=true,o++);}sr.length=1;const e=[...Sr.values()];for(const t of e)for(const e in t){const n=t[e][Tn];n&&(n.disposed=true,s++);}Sr.clear();}Ve$1(`forceDisposeProxies done: ${o} imports, ${s} exports, ${a} GCHandles, ${i} JSHandles.`);}function Xr(e){return Promise.resolve(e)===e||("object"==typeof e||"function"==typeof e)&&"function"==typeof e.then}function Qr(e){const{promise:t,promise_control:n}=_t();return e().then((e=>n.resolve(e))).catch((e=>n.reject(e))),t}const Yr=Symbol.for("wasm promise_holder");class Zr extends ManagedObject{constructor(e,t,n,r){super(),this.promise=e,this.gc_handle=t,this.promiseHolderPtr=n,this.res_converter=r,this.isResolved=false,this.isPosted=false,this.isPostponed=false,this.data=null,this.reason=void 0;}setIsResolving(){return  true}resolve(e){lt$1.is_runtime_running()?(this.isResolved&&mt(false,"resolve could be called only once"),this.isDisposed&&mt(false,"resolve is already disposed."),this.isResolved=true,this.complete_task_wrapper(e,null)):lt$1.diagnosticTracing&&ze$1("This promise resolution can't be propagated to managed code, mono runtime already exited.");}reject(e){lt$1.is_runtime_running()?(e||(e=new Error),this.isResolved&&mt(false,"reject could be called only once"),this.isDisposed&&mt(false,"resolve is already disposed."),e[Yr],this.isResolved=true,this.complete_task_wrapper(null,e)):lt$1.diagnosticTracing&&ze$1("This promise rejection can't be propagated to managed code, mono runtime already exited.");}cancel(){if(lt$1.is_runtime_running())if(this.isResolved&&mt(false,"cancel could be called only once"),this.isDisposed&&mt(false,"resolve is already disposed."),this.isPostponed)this.isResolved=true,void 0!==this.reason?this.complete_task_wrapper(null,this.reason):this.complete_task_wrapper(this.data,null);else {const e=this.promise;lt$1.assertIsControllablePromise(e);const t=lt$1.getPromiseController(e),n=new Error("OperationCanceledException");n[Yr]=this,t.reject(n);}else lt$1.diagnosticTracing&&ze$1("This promise cancelation can't be propagated to managed code, mono runtime already exited.");}complete_task_wrapper(e,t){try{this.isPosted&&mt(!1,"Promise is already posted to managed."),this.isPosted=!0,Vr(this,this.gc_handle,!0),function(e,t,n,r){lt$1.assert_runtime_running();const o=Ke$1.stackSave();try{const o=$n(5),s=Ln(o,2);Wn(s,14),Kn(s,e);const a=Ln(o,3);if(t)wo(a,t);else {Wn(a,0);const e=Ln(o,4);r||mt(!1,"res_converter missing"),r(e,n);}wn(ct$1.ioThreadTID,yn.CompleteTask,o);}finally{lt$1.is_runtime_running()&&Ke$1.stackRestore(o);}}(this.gc_handle,t,e,this.res_converter||So);}catch(e){try{lt$1.mono_exit(1,e);}catch(e){}}}}const Kr="For more information see https://aka.ms/dotnet-wasm-jsinterop";function eo(e,t,n){if(0===t||1===t||2===t||26===t)return;let r,o,s,a;o=Vt(Cn(e)),s=Vt(Dn(e)),a=Vt(Fn(e));const i=On(e);r=to(i),19===t&&(t=i);const c=to(t),l=Cn(e),p=n*In;return (e,t)=>{c(e+p,t,l,r,o,s,a);}}function to(e){if(0===e||1===e)return;const t=Un.get(e);return t&&"function"==typeof t||mt(false,`ERR30: Unknown converter for type ${e}`),t}function no(e,t){null==t?Wn(e,0):(Wn(e,3),qn(e,t));}function ro(e,t){null==t?Wn(e,0):(Wn(e,4),function(e,t){e||mt(false,"Null arg"),w$1(e,t);}(e,t));}function oo(e,t){null==t?Wn(e,0):(Wn(e,5),function(e,t){e||mt(false,"Null arg"),k$1(e,t);}(e,t));}function so(e,t){null==t?Wn(e,0):(Wn(e,6),function(e,t){e||mt(false,"Null arg"),U$1(e,t);}(e,t));}function ao(e,t){null==t?Wn(e,0):(Wn(e,7),function(e,t){e||mt(false,"Null arg"),T$1(e,t);}(e,t));}function io(e,t){null==t?Wn(e,0):(Wn(e,8),function(e,t){if(e||mt(false,"Null arg"),!Number.isSafeInteger(t))throw new Error(`Assert failed: Value is not an integer: ${t} (${typeof t})`);$$1(e,t);}(e,t));}function co(e,t){null==t?Wn(e,0):(Wn(e,9),function(e,t){e||mt(false,"Null arg"),A$1(e,t);}(e,t));}function lo(e,t){null==t?Wn(e,0):(Wn(e,10),Xn(e,t));}function po(e,t){null==t?Wn(e,0):(Wn(e,11),function(e,t){e||mt(false,"Null arg"),j$1(e,t);}(e,t));}function uo(e,t){null==t?Wn(e,0):(Wn(e,12),Gn(e,t));}function fo(e,t){if(null==t)Wn(e,0);else {if(!(t instanceof Date))throw new Error("Assert failed: Value is not a Date");Wn(e,17),Jn(e,t);}}function _o(e,t){if(null==t)Wn(e,0);else {if(!(t instanceof Date))throw new Error("Assert failed: Value is not a Date");Wn(e,18),Jn(e,t);}}function mo(e,t){if(null==t)Wn(e,0);else {if(Wn(e,15),"string"!=typeof t)throw new Error("Assert failed: Value is not a String");ho(e,t);}}function ho(e,t){{const n=er(e);try{!function(e,t){if(t.clear(),null!==e)if("symbol"==typeof e)Ce$1(e,t);else {if("string"!=typeof e)throw new Error("Expected string argument, got "+typeof e);if(0===e.length)Ce$1(e,t);else {if(e.length<=256){const n=be$1.get(e);if(n)return void t.set(n)}De$1(e,t);}}}(t,n);}finally{n.release();}}}function go(e){Wn(e,0);}function bo(e,t,r,o,s,a,i){if(null==t)return void Wn(e,0);if(!(t&&t instanceof Function))throw new Error("Assert failed: Value is not a Function");const c=function(e){const r=Ln(e,0),l=Ln(e,1),p=Ln(e,2),u=Ln(e,3),d=Ln(e,4),f=ct$1.isPendingSynchronousCall;try{let e,r,f;n$1&&c.isDisposed,s&&(e=s(p)),a&&(r=a(u)),i&&(f=i(d)),ct$1.isPendingSynchronousCall=!0;const _=t(e,r,f);o&&o(l,_);}catch(e){wo(r,e);}finally{ct$1.isPendingSynchronousCall=f;}};c[En]=true,c.isDisposed=false,c.dispose=()=>{c.isDisposed=true;},Yn(e,Pr(c)),Wn(e,25);}function yo(e,t,n,r){const o=30==zn(e);if(null==t)return void Wn(e,0);if(!Xr(t))throw new Error("Assert failed: Value is not a Promise");const s=o?Zn(e):$r.length?$r.pop():Lr--;o||(Kn(e,s),Wn(e,20));const a=new Zr(t,s,0,r);zr(a,s),t.then((e=>a.resolve(e)),(e=>a.reject(e)));}function wo(e,t){if(null==t)Wn(e,0);else if(t instanceof ManagedError)Wn(e,16),Kn(e,Wr(t));else {if("object"!=typeof t&&"string"!=typeof t)throw new Error("Assert failed: Value is not an Error "+typeof t);Wn(e,27),ho(e,t.toString());const n=t[Cr];Yn(e,n||Pr(t));}}function ko(e,t){if(null==t)Wn(e,0);else {if(void 0!==t[Or])throw new Error(`Assert failed: JSObject proxy of ManagedObject proxy is not supported. ${Kr}`);if("function"!=typeof t&&"object"!=typeof t)throw new Error(`Assert failed: JSObject proxy of ${typeof t} is not supported`);Wn(e,13),Yn(e,Pr(t));}}function So(e,t){if(null==t)Wn(e,0);else {const n=t[Or],r=typeof t;if(void 0===n)if("string"===r||"symbol"===r)Wn(e,15),ho(e,t);else if("number"===r)Wn(e,10),Xn(e,t);else {if("bigint"===r)throw new Error("NotImplementedException: bigint");if("boolean"===r)Wn(e,3),qn(e,t);else if(t instanceof Date)Wn(e,17),Jn(e,t);else if(t instanceof Error)wo(e,t);else if(t instanceof Uint8Array)Uo(e,t,4);else if(t instanceof Float64Array)Uo(e,t,10);else if(t instanceof Int32Array)Uo(e,t,7);else if(Array.isArray(t))Uo(e,t,14);else {if(t instanceof Int16Array||t instanceof Int8Array||t instanceof Uint8ClampedArray||t instanceof Uint16Array||t instanceof Uint32Array||t instanceof Float32Array)throw new Error("NotImplementedException: TypedArray");if(Xr(t))yo(e,t);else {if(t instanceof Span)throw new Error("NotImplementedException: Span");if("object"!=r)throw new Error(`JSObject proxy is not supported for ${r} ${t}`);{const n=Pr(t);Wn(e,13),Yn(e,n);}}}}else {if(Wr(t),t instanceof ArraySegment)throw new Error("NotImplementedException: ArraySegment. "+Kr);if(t instanceof ManagedError)Wn(e,16),Kn(e,n);else {if(!(t instanceof ManagedObject))throw new Error("NotImplementedException "+r+". "+Kr);Wn(e,14),Kn(e,n);}}}}function vo(e,t,n){n||mt(false,"Expected valid element_type parameter"),Uo(e,t,n);}function Uo(e,t,n){if(null==t)Wn(e,0);else {const r=rr(n);-1==r&&mt(false,`Element type ${n} not supported`);const s=t.length,a=r*s,i=_$1(a);if(15==n){if(!Array.isArray(t))throw new Error("Assert failed: Value is not an Array");g$1(i,a),o$1.mono_wasm_register_root(i,a,"marshal_array_to_cs");for(let e=0;e<s;e++)mo(Ln(i,e),t[e]);}else if(14==n){if(!Array.isArray(t))throw new Error("Assert failed: Value is not an Array");g$1(i,a),o$1.mono_wasm_register_root(i,a,"marshal_array_to_cs");for(let e=0;e<s;e++)So(Ln(i,e),t[e]);}else if(13==n){if(!Array.isArray(t))throw new Error("Assert failed: Value is not an Array");g$1(i,a);for(let e=0;e<s;e++)ko(Ln(i,e),t[e]);}else if(4==n){if(!(Array.isArray(t)||t instanceof Uint8Array))throw new Error("Assert failed: Value is not an Array or Uint8Array");K$1().subarray(i,i+s).set(t);}else if(7==n){if(!(Array.isArray(t)||t instanceof Int32Array))throw new Error("Assert failed: Value is not an Array or Int32Array");const e=oe$1(i,2);Y$1().subarray(e,e+s).set(t);}else {if(10!=n)throw new Error("not implemented");{if(!(Array.isArray(t)||t instanceof Float64Array))throw new Error("Assert failed: Value is not an Array or Float64Array");const e=oe$1(i,3);re$1().subarray(e,e+s).set(t);}}Gn(e,i),Wn(e,21),function(e,t){e||mt(false,"Null arg"),w$1(e+13,t);}(e,n),nr(e,t.length);}}function To(e,t,n){if(n||mt(false,"Expected valid element_type parameter"),t.isDisposed)throw new Error("Assert failed: ObjectDisposedException");xo(n,t._viewType),Wn(e,23),Gn(e,t._pointer),nr(e,t.length);}function Eo(e,t,n){n||mt(false,"Expected valid element_type parameter");const r=Wr(t);r||mt(false,"Only roundtrip of ArraySegment instance created by C#"),xo(n,t._viewType),Wn(e,22),Gn(e,t._pointer),nr(e,t.length),Kn(e,r);}function xo(e,t){if(4==e){if(0!=t)throw new Error("Assert failed: Expected MemoryViewType.Byte")}else if(7==e){if(1!=t)throw new Error("Assert failed: Expected MemoryViewType.Int32")}else {if(10!=e)throw new Error(`NotImplementedException ${e} `);if(2!=t)throw new Error("Assert failed: Expected MemoryViewType.Double")}}const Io={now:function(){return Date.now()}};function Ao(e){ void 0===globalThis.performance&&(globalThis.performance=Io),e.require=et$1.require,e.scriptDirectory=lt$1.scriptDirectory,Ke$1.locateFile===Ke$1.__locateFile&&(Ke$1.locateFile=lt$1.locateFile),e.fetch=lt$1.fetch_like,e.ENVIRONMENT_IS_WORKER=ot$1;}function jo(){if("function"!=typeof globalThis.fetch||"function"!=typeof globalThis.AbortController)throw new Error(tt$1?"Please install `node-fetch` and `node-abort-controller` npm packages to enable HTTP client support. See also https://aka.ms/dotnet-wasm-features":"This browser doesn't support fetch API. Please use a modern browser. See also https://aka.ms/dotnet-wasm-features")}function $o(){if("undefined"!=typeof Request&&"body"in Request.prototype&&"function"==typeof ReadableStream&&"function"==typeof TransformStream){let e=false;const t=new Request("",{body:new ReadableStream,method:"POST",get duplex(){return e=true,"half"}}).headers.has("Content-Type");return e&&!t}return  false}function Lo(){return "undefined"!=typeof Response&&"body"in Response.prototype&&"function"==typeof ReadableStream}function Ro(){return jo(),hr(),{abortController:new AbortController}}function Bo(e){e.catch((e=>{e&&"AbortError"!==e&&"AbortError"!==e.name&&ze$1("http muted: "+e);}));}function No(e){try{e.isAborted||(e.streamWriter&&(Bo(e.streamWriter.abort()),e.isAborted=!0),e.streamReader&&(Bo(e.streamReader.cancel()),e.isAborted=!0)),e.isAborted||e.abortController.signal.aborted||e.abortController.abort("AbortError");}catch(e){}}function Oo(e,t,n){n>0||mt(false,"expected bufferLength > 0");const r=new Span(t,n,0).slice();return Qr((async()=>{e.streamWriter||mt(false,"expected streamWriter"),e.responsePromise||mt(false,"expected fetch promise");try{await e.streamWriter.ready,await e.streamWriter.write(r);}catch(e){throw new Error("BrowserHttpWriteStream.Rejected")}}))}function Co(e){return e||mt(false,"expected controller"),Qr((async()=>{e.streamWriter||mt(false,"expected streamWriter"),e.responsePromise||mt(false,"expected fetch promise");try{await e.streamWriter.ready,await e.streamWriter.close();}catch(e){throw new Error("BrowserHttpWriteStream.Rejected")}}))}function Do(e,t,n,r,o,s){const a=new TransformStream;return e.streamWriter=a.writable.getWriter(),Bo(e.streamWriter.closed),Bo(e.streamWriter.ready),Po(e,t,n,r,o,s,a.readable)}function Fo(e,t,n,r,o,s,a,i){return Po(e,t,n,r,o,s,new Span(a,i,0).slice())}function Po(e,t,n,r,o,s,a){jo(),hr(),t&&"string"==typeof t||mt(false,"expected url string"),n&&r&&Array.isArray(n)&&Array.isArray(r)&&n.length===r.length||mt(false,"expected headerNames and headerValues arrays"),o&&s&&Array.isArray(o)&&Array.isArray(s)&&o.length===s.length||mt(false,"expected headerNames and headerValues arrays");const i=new Headers;for(let e=0;e<n.length;e++)i.append(n[e],r[e]);const c={body:a,headers:i,signal:e.abortController.signal};"undefined"!=typeof ReadableStream&&a instanceof ReadableStream&&(c.duplex="half");for(let e=0;e<o.length;e++)c[o[e]]=s[e];return e.responsePromise=Qr((()=>lt$1.fetch_like(t,c).then((t=>(e.response=t,null))))),e.responsePromise.then((()=>{if(e.response||mt(false,"expected response"),e.responseHeaderNames=[],e.responseHeaderValues=[],e.response.headers&&e.response.headers.entries){const t=e.response.headers.entries();for(const n of t)e.responseHeaderNames.push(n[0]),e.responseHeaderValues.push(n[1]);}})).catch((()=>{})),e.responsePromise}function Mo(e){var t;return null===(t=e.response)||void 0===t?void 0:t.type}function zo(e){var t,n;return null!==(n=null===(t=e.response)||void 0===t?void 0:t.status)&&void 0!==n?n:0}function Vo(e){return e.responseHeaderNames||mt(false,"expected responseHeaderNames"),e.responseHeaderNames}function Wo(e){return e.responseHeaderValues||mt(false,"expected responseHeaderValues"),e.responseHeaderValues}function Ho(e){return Qr((async()=>{const t=await e.response.arrayBuffer();return e.responseBuffer=t,e.currentBufferOffset=0,t.byteLength}))}function qo(e,t){if(e||mt(false,"expected controller"),e.responseBuffer||mt(false,"expected resoved arrayBuffer"),null==e.currentBufferOffset&&mt(false,"expected currentBufferOffset"),e.currentBufferOffset==e.responseBuffer.byteLength)return 0;const n=new Uint8Array(e.responseBuffer,e.currentBufferOffset);t.set(n,0);const r=Math.min(t.byteLength,n.byteLength);return e.currentBufferOffset+=r,r}function Go(e,t,n){const r=new Span(t,n,0);return Qr((async()=>{if(await e.responsePromise,e.response||mt(false,"expected response"),!e.response.body)return 0;if(e.streamReader||(e.streamReader=e.response.body.getReader(),Bo(e.streamReader.closed)),e.currentStreamReaderChunk&&void 0!==e.currentBufferOffset||(e.currentStreamReaderChunk=await e.streamReader.read(),e.currentBufferOffset=0),e.currentStreamReaderChunk.done){if(e.isAborted)throw new Error("OperationCanceledException");return 0}const t=e.currentStreamReaderChunk.value.byteLength-e.currentBufferOffset;t>0||mt(false,"expected remaining_source to be greater than 0");const n=Math.min(t,r.byteLength),o=e.currentStreamReaderChunk.value.subarray(e.currentBufferOffset,e.currentBufferOffset+n);return r.set(o,0),e.currentBufferOffset+=n,t==n&&(e.currentStreamReaderChunk=void 0),n}))}let Jo,Xo,Qo=0;function Yo(){if(!lt$1.isChromium)return;const e=(new Date).valueOf(),t=e+36e4;for(let n=Math.max(e+1e3,Qo);n<t;n+=1e3){const t=n-e;globalThis.setTimeout(Zo,t);}Qo=t;}function Zo(){if(Ke$1.maybeExit(),lt$1.is_runtime_running()){try{o$1.mono_wasm_execute_timer();}catch(e){lt$1.mono_exit(1,e);}Ko();}}function Ko(){if(Jo=void 0,Ke$1.maybeExit(),lt$1.is_runtime_running())try{o$1.mono_background_exec();}catch(e){lt$1.mono_exit(1,e);}}function mono_wasm_schedule_timer_tick(){if(Ke$1.maybeExit(),lt$1.is_runtime_running()){Xo=void 0;try{o$1.mono_wasm_execute_timer();}catch(e){lt$1.mono_exit(1,e);}}}class es{constructor(){this.queue=[],this.offset=0;}getLength(){return this.queue.length-this.offset}isEmpty(){return 0==this.queue.length}enqueue(e){this.queue.push(e);}dequeue(){if(0===this.queue.length)return;const e=this.queue[this.offset];return this.queue[this.offset]=null,2*++this.offset>=this.queue.length&&(this.queue=this.queue.slice(this.offset),this.offset=0),e}peek(){return this.queue.length>0?this.queue[this.offset]:void 0}drain(e){for(;this.getLength();)e(this.dequeue());}}const ts=Symbol.for("wasm ws_pending_send_buffer"),ns=Symbol.for("wasm ws_pending_send_buffer_offset"),rs=Symbol.for("wasm ws_pending_send_buffer_type"),os=Symbol.for("wasm ws_pending_receive_event_queue"),ss=Symbol.for("wasm ws_pending_receive_promise_queue"),as=Symbol.for("wasm ws_pending_open_promise"),is=Symbol.for("wasm wasm_ws_pending_open_promise_used"),cs=Symbol.for("wasm wasm_ws_pending_error"),ls=Symbol.for("wasm ws_pending_close_promises"),ps=Symbol.for("wasm ws_pending_send_promises"),us=Symbol.for("wasm ws_is_aborted"),ds=Symbol.for("wasm wasm_ws_close_sent"),fs=Symbol.for("wasm wasm_ws_close_received"),_s=Symbol.for("wasm ws_receive_status_ptr"),ms=65536,hs=new Uint8Array;function gs(e){var t,n;return e.readyState!=WebSocket.CLOSED?null!==(t=e.readyState)&&void 0!==t?t:-1:0==e[os].getLength()?null!==(n=e.readyState)&&void 0!==n?n:-1:e[ds]?WebSocket.CLOSING:WebSocket.OPEN}function bs(e,t,n){let r;!function(){if(at$1)throw new Error("WebSockets are not supported in shell JS engine.");if("function"!=typeof globalThis.WebSocket)throw new Error(tt$1?"Please install `ws` npm package to enable networking support. See also https://aka.ms/dotnet-wasm-features":"This browser doesn't support WebSocket API. Please use a modern browser. See also https://aka.ms/dotnet-wasm-features")}(),hr(),e&&"string"==typeof e||mt(false,"ERR12: Invalid uri "+typeof e);try{r=new globalThis.WebSocket(e,t||void 0);}catch(e){throw We$1("WebSocket error in ws_wasm_create: "+e.toString()),e}const{promise_control:o}=_t();r[os]=new es,r[ss]=new es,r[as]=o,r[ps]=[],r[ls]=[],r[_s]=n,r.binaryType="arraybuffer";const s=()=>{try{if(r[us])return;if(!lt$1.is_runtime_running())return;o.resolve(r),Yo();}catch(e){We$1("failed to propagate WebSocket open event: "+e.toString());}},a=e=>{try{if(r[us])return;if(!lt$1.is_runtime_running())return;!function(e,t){const n=e[os],r=e[ss];if("string"==typeof t.data)n.enqueue({type:0,data:je$1(t.data),offset:0});else {if("ArrayBuffer"!==t.data.constructor.name)throw new Error("ERR19: WebSocket receive expected ArrayBuffer");n.enqueue({type:1,data:new Uint8Array(t.data),offset:0});}if(r.getLength()&&n.getLength()>1)throw new Error("ERR21: Invalid WS state");for(;r.getLength()&&n.getLength();){const t=r.dequeue();Ts(e,n,t.buffer_ptr,t.buffer_length),t.resolve();}Yo();}(r,e),Yo();}catch(e){We$1("failed to propagate WebSocket message event: "+e.toString());}},i=e=>{try{if(r.removeEventListener("message",a),r[us])return;if(!lt$1.is_runtime_running())return;r[fs]=!0,r.close_status=e.code,r.close_status_description=e.reason,r[is]&&o.reject(new Error(e.reason));for(const e of r[ls])e.resolve();Ke$1.safeSetTimeout((()=>{r[ss].drain((e=>{T$1(n,0),T$1(n+4,2),T$1(n+8,1),e.resolve();}));}),0);}catch(e){We$1("failed to propagate WebSocket close event: "+e.toString());}},c=e=>{try{if(r[us])return;if(!lt$1.is_runtime_running())return;r.removeEventListener("message",a);const t=e.message?"WebSocket error: "+e.message:"WebSocket error";We$1(t),r[cs]=t,Us(r,new Error(t));}catch(e){We$1("failed to propagate WebSocket error event: "+e.toString());}};return r.addEventListener("message",a),r.addEventListener("open",s,{once:true}),r.addEventListener("close",i,{once:true}),r.addEventListener("error",c,{once:true}),r.dispose=()=>{r.removeEventListener("message",a),r.removeEventListener("open",s),r.removeEventListener("close",i),r.removeEventListener("error",c),vs(r);},r}function ys(e){if(e||mt(false,"ERR17: expected ws instance"),e[cs])return Es(e[cs]);const t=e[as];return e[is]=true,t.promise}function ws(e,t,n,r,o){if(e||mt(false,"ERR17: expected ws instance"),e[cs])return Es(e[cs]);if(e[us]||e[ds])return Es("InvalidState: The WebSocket is not connected.");if(e.readyState==WebSocket.CLOSED)return null;const s=function(e,t,n,r){let o=e[ts],s=0;const a=t.byteLength;if(o){if(s=e[ns],n=e[rs],0!==a){if(s+a>o.length){const n=new Uint8Array(1.5*(s+a+50));n.set(o,0),n.subarray(s).set(t),e[ts]=o=n;}else o.subarray(s).set(t);s+=a,e[ns]=s;}}else r?0!==a&&(o=t,s=a):(0!==a&&(o=t.slice(),s=a,e[ns]=s,e[ts]=o),e[rs]=n);return r?0==s||null==o?hs:0===n?function(e){return void 0===ve$1?Ke$1.UTF8ArrayToString(e,0,e.byteLength):ve$1.decode(e)}(Fe$1(o,0,s)):o.subarray(0,s):null}(e,new Uint8Array(K$1().buffer,t,n),r,o);return o&&s?function(e,t){if(e.send(t),e[ts]=null,e.bufferedAmount<ms)return null;const{promise:n,promise_control:r}=_t(),o=e[ps];o.push(r);let s=1;const a=()=>{try{if(0===e.bufferedAmount)r.resolve();else {const t=e.readyState;if(t!=WebSocket.OPEN&&t!=WebSocket.CLOSING)r.reject(new Error(`InvalidState: ${t} The WebSocket is not connected.`));else if(!r.isDone)return globalThis.setTimeout(a,s),void(s=Math.min(1.5*s,1e3))}const t=o.indexOf(r);t>-1&&o.splice(t,1);}catch(e){We$1("WebSocket error in web_socket_send_and_wait: "+e.toString()),r.reject(e);}};return globalThis.setTimeout(a,0),n}(e,s):null}function ks(e,t,n){if(e||mt(false,"ERR18: expected ws instance"),e[cs])return Es(e[cs]);if(e[us]){const t=e[_s];return T$1(t,0),T$1(t+4,2),T$1(t+8,1),null}const r=e[os],o=e[ss];if(r.getLength())return 0!=o.getLength()&&mt(false,"ERR20: Invalid WS state"),Ts(e,r,t,n),null;if(e[fs]){const t=e[_s];return T$1(t,0),T$1(t+4,2),T$1(t+8,1),null}const{promise:s,promise_control:a}=_t(),i=a;return i.buffer_ptr=t,i.buffer_length=n,o.enqueue(i),s}function Ss(e,t,n,r){if(e||mt(false,"ERR19: expected ws instance"),e[us]||e[ds]||e.readyState==WebSocket.CLOSED)return null;if(e[cs])return Es(e[cs]);if(e[ds]=true,r){const{promise:r,promise_control:o}=_t();return e[ls].push(o),"string"==typeof n?e.close(t,n):e.close(t),r}return "string"==typeof n?e.close(t,n):e.close(t),null}function vs(e){if(e||mt(false,"ERR18: expected ws instance"),!e[us]&&!e[ds]){e[us]=true,Us(e,new Error("OperationCanceledException"));try{e.close(1e3,"Connection was aborted.");}catch(e){We$1("WebSocket error in ws_wasm_abort: "+e.toString());}}}function Us(e,t){const n=e[as],r=e[is];n&&r&&n.reject(t);for(const n of e[ls])n.reject(t);for(const n of e[ps])n.reject(t);e[ss].drain((e=>{e.reject(t);}));}function Ts(e,t,n,r){const o=t.peek(),s=Math.min(r,o.data.length-o.offset);if(s>0){const e=o.data.subarray(o.offset,o.offset+s);new Uint8Array(K$1().buffer,n,r).set(e,0),o.offset+=s;}const a=o.data.length===o.offset?1:0;a&&t.dequeue();const i=e[_s];T$1(i,s),T$1(i+4,o.type),T$1(i+8,a);}function Es(e){return function(e){const{promise:t,promise_control:n}=_t();return e.then((e=>n.resolve(e))).catch((e=>n.reject(e))),t}(Promise.reject(new Error(e)))}function xs(e,t,n){lt$1.diagnosticTracing&&ze$1(`Loaded:${e.name} as ${e.behavior} size ${n.length} from ${t}`);const r=Ft(),s="string"==typeof e.virtualPath?e.virtualPath:e.name;let a=null;switch(e.behavior){case "dotnetwasm":case "js-module-threads":case "js-module-diagnostics":case "symbols":break;case "resource":case "assembly":case "pdb":lt$1._loaded_files.push({url:t,file:s});case "heap":case "icu":a=function(e){const t=e.length+16;let n=Ke$1._sbrk(t);if(n<=0){if(n=Ke$1._sbrk(t),n<=0)throw He$1(`sbrk failed to allocate ${t} bytes, and failed upon retry.`),new Error("Out of memory");We$1(`sbrk failed to allocate ${t} bytes, but succeeded upon retry!`);}return new Uint8Array(K$1().buffer,n,e.length).set(e),n}(n);break;case "vfs":{const e=s.lastIndexOf("/");let t=e>0?s.substring(0,e):null,r=e>0?s.substring(e+1):s;r.startsWith("/")&&(r=r.substring(1)),t?(t.startsWith("/")||(t="/"+t),ze$1(`Creating directory '${t}'`),Ke$1.FS_createPath("/",t,true,true)):t="/",lt$1.diagnosticTracing&&ze$1(`Creating file '${r}' in directory '${t}'`),Ke$1.FS_createDataFile(t,r,n,true,true,true);break}default:throw new Error(`Unrecognized asset behavior:${e.behavior}, for asset ${e.name}`)}if("assembly"===e.behavior){if(!o$1.mono_wasm_add_assembly(s,a,n.length)){const e=lt$1._loaded_files.findIndex((e=>e.file==s));lt$1._loaded_files.splice(e,1);}}else "pdb"===e.behavior?o$1.mono_wasm_add_assembly(s,a,n.length):"icu"===e.behavior?function(e){if(!o$1.mono_wasm_load_icu_data(e))throw new Error("Failed to load ICU data")}(a):"resource"===e.behavior&&o$1.mono_wasm_add_satellite_assembly(s,e.culture||"",a,n.length);Pt(r,"mono.instantiateAsset:",e.name),++lt$1.actual_instantiated_assets_count;}async function Is(e){try{const n=await e.pendingDownloadInternal.response;t=await n.text(),Ge$1&&mt(!1,"Another symbol map was already loaded"),Ge$1=t,lt$1.diagnosticTracing&&ze$1(`Deferred loading of ${t.length}ch symbol map`);}catch(t){Ve$1(`Error loading symbol file ${e.name}: ${JSON.stringify(t)}`);}var t;}function As(){return lt$1.loadedFiles}const js={};function $s(e){let t=js[e];if("string"!=typeof t){const n=o$1.mono_jiterp_get_opcode_info(e,0);js[e]=t=Le$1(n);}return t}const Ls=2,Rs=64,Bs=64,Ns=-2147483648,Os={};class Cs{constructor(e){this.locals=new Map,this.permanentFunctionTypeCount=0,this.permanentFunctionTypes={},this.permanentFunctionTypesByShape={},this.permanentFunctionTypesByIndex={},this.functionTypesByIndex={},this.permanentImportedFunctionCount=0,this.permanentImportedFunctions={},this.nextImportIndex=0,this.functions=[],this.estimatedExportBytes=0,this.frame=0,this.traceBuf=[],this.branchTargets=new Set,this.constantSlots=[],this.backBranchOffsets=[],this.callHandlerReturnAddresses=[],this.nextConstantSlot=0,this.backBranchTraceLevel=0,this.compressImportNames=false,this.lockImports=false,this._assignParameterIndices=e=>{let t=0;for(const n in e)this.locals.set(n,t),t++;return t},this.stack=[new Ds],this.clear(e),this.cfg=new Fs(this),this.defineType("__cpp_exception",{ptr:127},64,true);}clear(e){if(this.options=fa(),this.options.maxModuleSize>=24576)throw new Error(`blobBuilderCapacity 24576 is not large enough for jiterpreter-max-module-size of ${this.options.maxModuleSize}`);this.stackSize=1,this.inSection=false,this.inFunction=false,this.lockImports=false,this.locals.clear(),this.functionTypeCount=this.permanentFunctionTypeCount,this.functionTypes=Object.create(this.permanentFunctionTypes),this.functionTypesByShape=Object.create(this.permanentFunctionTypesByShape),this.functionTypesByIndex=Object.create(this.permanentFunctionTypesByIndex),this.nextImportIndex=0,this.importedFunctionCount=0,this.importedFunctions=Object.create(this.permanentImportedFunctions);for(const e in this.importedFunctions)this.importedFunctions[e].index=void 0;this.functions.length=0,this.estimatedExportBytes=0,this.argumentCount=0,this.current.clear(),this.traceBuf.length=0,this.branchTargets.clear(),this.activeBlocks=0,this.nextConstantSlot=0,this.constantSlots.length=this.options.useConstants?e:0;for(let e=0;e<this.constantSlots.length;e++)this.constantSlots[e]=0;this.backBranchOffsets.length=0,this.callHandlerReturnAddresses.length=0,this.allowNullCheckOptimization=this.options.eliminateNullChecks,this.containsSimd=false,this.containsAtomics=false;}_push(){this.stackSize++,this.stackSize>=this.stack.length&&this.stack.push(new Ds),this.current.clear();}_pop(e){if(this.stackSize<=1)throw new Error("Stack empty");const t=this.current;return this.stackSize--,e?(this.appendULeb(t.size),t.copyTo(this.current),null):t.getArrayView(false).slice(0,t.size)}setImportFunction(e,t){const n=this.importedFunctions[e];if(!n)throw new Error("No import named "+e);n.func=t;}getExceptionTag(){const e=Ke$1.wasmExports.__cpp_exception;return void 0!==e&&(e instanceof WebAssembly.Tag||mt(false,`expected __cpp_exception export from dotnet.wasm to be WebAssembly.Tag but was ${e}`)),e}getWasmImports(){const e=ct$1.getMemory();e instanceof WebAssembly.Memory||mt(false,`expected heap import to be WebAssembly.Memory but was ${e}`);const t=this.getExceptionTag(),n={c:this.getConstants(),m:{h:e}};t&&(n.x={e:t});const r=this.getImportsToEmit();for(let e=0;e<r.length;e++){const t=r[e];if("function"!=typeof t.func)throw new Error(`Import '${t.name}' not found or not a function`);const o=this.getCompressedName(t);let s=n[t.module];s||(s=n[t.module]={}),s[o]=t.func;}return n}get bytesGeneratedSoFar(){const e=this.compressImportNames?8:20;return this.stack[0].size+32+this.importedFunctionCount*e+2*this.functions.length+this.estimatedExportBytes}get current(){return this.stack[this.stackSize-1]}get size(){return this.current.size}appendU8(e){if(e!=e>>>0||e>255)throw new Error(`Byte out of range: ${e}`);return this.current.appendU8(e)}appendSimd(e,t){return this.current.appendU8(253),0|e||0===e&&true===t||mt(false,"Expected non-v128_load simd opcode or allowLoad==true"),this.current.appendULeb(e)}appendAtomic(e,t){return this.current.appendU8(254),0|e||0===e&&true===t||mt(false,"Expected non-notify atomic opcode or allowNotify==true"),this.current.appendU8(e)}appendU32(e){return this.current.appendU32(e)}appendF32(e){return this.current.appendF32(e)}appendF64(e){return this.current.appendF64(e)}appendBoundaryValue(e,t){return this.current.appendBoundaryValue(e,t)}appendULeb(e){return this.current.appendULeb(e)}appendLeb(e){return this.current.appendLeb(e)}appendLebRef(e,t){return this.current.appendLebRef(e,t)}appendBytes(e){return this.current.appendBytes(e)}appendName(e){return this.current.appendName(e)}ret(e){this.ip_const(e),this.appendU8(15);}i32_const(e){this.appendU8(65),this.appendLeb(e);}ptr_const(e){let t=this.options.useConstants?this.constantSlots.indexOf(e):-1;this.options.useConstants&&t<0&&this.nextConstantSlot<this.constantSlots.length&&(t=this.nextConstantSlot++,this.constantSlots[t]=e),t>=0?(this.appendU8(35),this.appendLeb(t)):this.i32_const(e);}ip_const(e){this.appendU8(65),this.appendLeb(e-this.base);}i52_const(e){this.appendU8(66),this.appendLeb(e);}v128_const(e){if(0===e)this.local("v128_zero");else {if("object"!=typeof e)throw new Error("Expected v128_const arg to be 0 or a Uint8Array");{16!==e.byteLength&&mt(false,"Expected v128_const arg to be 16 bytes in size");let t=true;for(let n=0;n<16;n++)0!==e[n]&&(t=false);t?this.local("v128_zero"):(this.appendSimd(12),this.appendBytes(e));}}}defineType(e,t,n,r){if(this.functionTypes[e])throw new Error(`Function type ${e} already defined`);if(r&&this.functionTypeCount>this.permanentFunctionTypeCount)throw new Error("New permanent function types cannot be defined after non-permanent ones");let o="";for(const e in t)o+=t[e]+",";o+=n;let s=this.functionTypesByShape[o];"number"!=typeof s&&(s=this.functionTypeCount++,r?(this.permanentFunctionTypeCount++,this.permanentFunctionTypesByShape[o]=s,this.permanentFunctionTypesByIndex[s]=[t,Object.values(t).length,n]):(this.functionTypesByShape[o]=s,this.functionTypesByIndex[s]=[t,Object.values(t).length,n]));const a=[s,t,n,`(${JSON.stringify(t)}) -> ${n}`,r];return r?this.permanentFunctionTypes[e]=a:this.functionTypes[e]=a,s}generateTypeSection(){this.beginSection(1),this.appendULeb(this.functionTypeCount);for(let e=0;e<this.functionTypeCount;e++){const t=this.functionTypesByIndex[e][0],n=this.functionTypesByIndex[e][1],r=this.functionTypesByIndex[e][2];this.appendU8(96),this.appendULeb(n);for(const e in t)this.appendU8(t[e]);64!==r?(this.appendULeb(1),this.appendU8(r)):this.appendULeb(0);}this.endSection();}getImportedFunctionTable(){const e={};for(const t in this.importedFunctions){const n=this.importedFunctions[t];e[this.getCompressedName(n)]=n.func;}return e}getCompressedName(e){if(!this.compressImportNames||"number"!=typeof e.index)return e.name;let t=Os[e.index];return "string"!=typeof t&&(Os[e.index]=t=e.index.toString(36)),t}getImportsToEmit(){const e=[];for(const t in this.importedFunctions){const n=this.importedFunctions[t];"number"==typeof n.index&&e.push(n);}return e.sort(((e,t)=>e.index-t.index)),e}_generateImportSection(e){const t=this.getImportsToEmit();if(this.lockImports=true,false!==e)throw new Error("function table imports are disabled");const n=void 0!==this.getExceptionTag();this.beginSection(2),this.appendULeb(1+(n?1:0)+t.length+this.constantSlots.length+(false!==e?1:0));for(let e=0;e<t.length;e++){const n=t[e];this.appendName(n.module),this.appendName(this.getCompressedName(n)),this.appendU8(0),this.appendU8(n.typeIndex);}for(let e=0;e<this.constantSlots.length;e++)this.appendName("c"),this.appendName(e.toString(36)),this.appendU8(3),this.appendU8(127),this.appendU8(0);this.appendName("m"),this.appendName("h"),this.appendU8(2),this.appendU8(0),this.appendULeb(1),n&&(this.appendName("x"),this.appendName("e"),this.appendU8(4),this.appendU8(0),this.appendULeb(this.getTypeIndex("__cpp_exception"))),false!==e&&(this.appendName("f"),this.appendName("f"),this.appendU8(1),this.appendU8(112),this.appendU8(0),this.appendULeb(1));}defineImportedFunction(e,t,n,r,o){if(this.lockImports)throw new Error("Import section already generated");if(r&&this.importedFunctionCount>0)throw new Error("New permanent imports cannot be defined after any indexes have been assigned");const s=this.functionTypes[n];if(!s)throw new Error("No function type named "+n);if(r&&!s[4])throw new Error("A permanent import must have a permanent function type");const a=s[0],i=r?this.permanentImportedFunctions:this.importedFunctions;if("number"==typeof o&&(o=qs().get(o)),"function"!=typeof o&&void 0!==o)throw new Error(`Value passed for imported function ${t} was not a function or valid function pointer or undefined`);return i[t]={index:void 0,typeIndex:a,module:e,name:t,func:o}}markImportAsUsed(e){const t=this.importedFunctions[e];if(!t)throw new Error("No imported function named "+e);"number"!=typeof t.index&&(t.index=this.importedFunctionCount++);}getTypeIndex(e){const t=this.functionTypes[e];if(!t)throw new Error("No type named "+e);return t[0]}defineFunction(e,t){const n={index:this.functions.length,name:e.name,typeName:e.type,typeIndex:this.getTypeIndex(e.type),export:e.export,locals:e.locals,generator:t,error:null,blob:null};return this.functions.push(n),n.export&&(this.estimatedExportBytes+=n.name.length+8),n}emitImportsAndFunctions(e){let t=0;for(let e=0;e<this.functions.length;e++){const n=this.functions[e];n.export&&t++,this.beginFunction(n.typeName,n.locals);try{n.blob=n.generator();}finally{try{n.blob||(n.blob=this.endFunction(!1));}catch(e){}}}this._generateImportSection(e),this.beginSection(3),this.appendULeb(this.functions.length);for(let e=0;e<this.functions.length;e++)this.appendULeb(this.functions[e].typeIndex);this.beginSection(7),this.appendULeb(t);for(let e=0;e<this.functions.length;e++){const t=this.functions[e];t.export&&(this.appendName(t.name),this.appendU8(0),this.appendULeb(this.importedFunctionCount+e));}this.beginSection(10),this.appendULeb(this.functions.length);for(let e=0;e<this.functions.length;e++){const t=this.functions[e];t.blob||mt(false,`expected function ${t.name} to have a body`),this.appendULeb(t.blob.length),this.appendBytes(t.blob);}this.endSection();}call_indirect(){throw new Error("call_indirect unavailable")}callImport(e){const t=this.importedFunctions[e];if(!t)throw new Error("No imported function named "+e);if("number"!=typeof t.index){if(this.lockImports)throw new Error("Import section was emitted before assigning an index to import named "+e);t.index=this.importedFunctionCount++;}this.appendU8(16),this.appendULeb(t.index);}beginSection(e){this.inSection&&this._pop(true),this.appendU8(e),this._push(),this.inSection=true;}endSection(){if(!this.inSection)throw new Error("Not in section");this.inFunction&&this.endFunction(true),this._pop(true),this.inSection=false;}_assignLocalIndices(e,t,n,r){e[127]=0,e[126]=0,e[125]=0,e[124]=0,e[123]=0;for(const n in t){const o=t[n];e[o]<=0&&r++,e[o]++;}const o=e[127],s=o+e[126],a=s+e[125],i=a+e[124];e[127]=0,e[126]=0,e[125]=0,e[124]=0,e[123]=0;for(const r in t){const c=t[r];let l,p=0;switch(c){case 127:l=0;break;case 126:l=o;break;case 125:l=s;break;case 124:l=a;break;case 123:l=i;break;default:throw new Error(`Unimplemented valtype: ${c}`)}p=e[c]+++l+n,this.locals.set(r,p);}return r}beginFunction(e,t){if(this.inFunction)throw new Error("Already in function");this._push();const n=this.functionTypes[e];this.locals.clear(),this.branchTargets.clear();let r={};const o=[127,126,125,124,123];let s=0;const a=this._assignParameterIndices(n[1]);t?s=this._assignLocalIndices(r,t,a,s):r={},this.appendULeb(s);for(let e=0;e<o.length;e++){const t=o[e],n=r[t];n&&(this.appendULeb(n),this.appendU8(t));}this.inFunction=true;}endFunction(e){if(!this.inFunction)throw new Error("Not in function");if(this.activeBlocks>0)throw new Error(`${this.activeBlocks} unclosed block(s) at end of function`);const t=this._pop(e);return this.inFunction=false,t}block(e,t){const n=this.appendU8(t||2);return e?this.appendU8(e):this.appendU8(64),this.activeBlocks++,n}endBlock(){if(this.activeBlocks<=0)throw new Error("No blocks active");this.activeBlocks--,this.appendU8(11);}arg(e,t){const n="string"==typeof e?this.locals.has(e)?this.locals.get(e):void 0:e;if("number"!=typeof n)throw new Error("No local named "+e);t&&this.appendU8(t),this.appendULeb(n);}local(e,t){const n="string"==typeof e?this.locals.has(e)?this.locals.get(e):void 0:e+this.argumentCount;if("number"!=typeof n)throw new Error("No local named "+e);t?this.appendU8(t):this.appendU8(32),this.appendULeb(n);}appendMemarg(e,t){this.appendULeb(t),this.appendULeb(e);}lea(e,t){"string"==typeof e?this.local(e):this.i32_const(e),this.i32_const(t),this.appendU8(106);}getArrayView(e,t){if(true!==t&&this.stackSize>1)throw new Error("Jiterpreter block stack not empty");return this.stack[0].getArrayView(e)}getConstants(){const e={};for(let t=0;t<this.constantSlots.length;t++)e[t.toString(36)]=this.constantSlots[t];return e}}class Ds{constructor(){this.textBuf=new Uint8Array(1024),this.capacity=24576,this.buffer=_$1(this.capacity),this.buffer||mt(false,"Failed to allocate 24576b buffer for BlobBuilder"),K$1().fill(0,this.buffer,this.buffer+this.capacity),this.size=0,this.clear(),"function"==typeof TextEncoder&&(this.encoder=new TextEncoder);}clear(){this.size=0;}appendU8(e){if(this.size>=this.capacity)throw new Error("Buffer full");const t=this.size;return K$1()[this.buffer+this.size++]=e,t}appendU32(e){const t=this.size;return o$1.mono_jiterp_write_number_unaligned(this.buffer+this.size,e,0),this.size+=4,t}appendI32(e){const t=this.size;return o$1.mono_jiterp_write_number_unaligned(this.buffer+this.size,e,1),this.size+=4,t}appendF32(e){const t=this.size;return o$1.mono_jiterp_write_number_unaligned(this.buffer+this.size,e,2),this.size+=4,t}appendF64(e){const t=this.size;return o$1.mono_jiterp_write_number_unaligned(this.buffer+this.size,e,3),this.size+=8,t}appendBoundaryValue(e,t){if(this.size+8>=this.capacity)throw new Error("Buffer full");const n=o$1.mono_jiterp_encode_leb_signed_boundary(this.buffer+this.size,e,t);if(n<1)throw new Error(`Failed to encode ${e} bit boundary value with sign ${t}`);return this.size+=n,n}appendULeb(e){if("number"!=typeof e&&mt(false,`appendULeb expected number but got ${e}`),e>=0||mt(false,"cannot pass negative value to appendULeb"),e<127){if(this.size+1>=this.capacity)throw new Error("Buffer full");return this.appendU8(e),1}if(this.size+8>=this.capacity)throw new Error("Buffer full");const t=o$1.mono_jiterp_encode_leb52(this.buffer+this.size,e,0);if(t<1)throw new Error(`Failed to encode value '${e}' as unsigned leb`);return this.size+=t,t}appendLeb(e){if("number"!=typeof e&&mt(false,`appendLeb expected number but got ${e}`),this.size+8>=this.capacity)throw new Error("Buffer full");const t=o$1.mono_jiterp_encode_leb52(this.buffer+this.size,e,1);if(t<1)throw new Error(`Failed to encode value '${e}' as signed leb`);return this.size+=t,t}appendLebRef(e,t){if(this.size+8>=this.capacity)throw new Error("Buffer full");const n=o$1.mono_jiterp_encode_leb64_ref(this.buffer+this.size,e,t?1:0);if(n<1)throw new Error("Failed to encode value as leb");return this.size+=n,n}copyTo(e,t){if("number"!=typeof t&&(t=this.size),e.size+t>=e.capacity)throw new Error("Destination buffer full");K$1().copyWithin(e.buffer+e.size,this.buffer,this.buffer+t),e.size+=t;}appendBytes(e,t){const n=this.size,r=K$1(),o="number"!=typeof t?e.length:t;if(this.size+o>=this.capacity)throw new Error("Buffer full");return e.buffer===r.buffer?(r.copyWithin(this.buffer+n,e.byteOffset,e.byteOffset+o),this.size+=o):("number"==typeof t&&(e=new Uint8Array(e.buffer,e.byteOffset,t)),this.getArrayView(true).set(e,this.size),this.size+=e.length),n}appendName(e){let t=e.length,n=1===e.length?e.charCodeAt(0):-1;if(n>127&&(n=-1),t&&n<0)if(this.encoder)t=this.encoder.encodeInto(e,this.textBuf).written||0;else for(let n=0;n<t;n++){const t=e.charCodeAt(n);if(t>127)throw new Error("Out of range character and no TextEncoder available");this.textBuf[n]=t;}this.appendULeb(t),n>=0?this.appendU8(n):t>1&&this.appendBytes(this.textBuf,t);}getArrayView(e){return new Uint8Array(K$1().buffer,this.buffer,e?this.capacity:this.size)}}class Fs{constructor(e){this.segments=[],this.backBranchTargets=null,this.lastSegmentEnd=0,this.overheadBytes=0,this.blockStack=[],this.backDispatchOffsets=[],this.dispatchTable=new Map,this.observedBackBranchTargets=new Set,this.trace=0,this.builder=e;}initialize(e,t,n){this.segments.length=0,this.blockStack.length=0,this.startOfBody=e,this.backBranchTargets=t,this.base=this.builder.base,this.ip=this.lastSegmentStartIp=this.firstOpcodeIp=this.builder.base,this.lastSegmentEnd=0,this.overheadBytes=10,this.dispatchTable.clear(),this.observedBackBranchTargets.clear(),this.trace=n,this.backDispatchOffsets.length=0;}entry(e){this.entryIp=e;const t=o$1.mono_jiterp_get_opcode_info(676,1);return this.firstOpcodeIp=e+2*t,this.appendBlob(),1!==this.segments.length&&mt(false,"expected 1 segment"),"blob"!==this.segments[0].type&&mt(false,"expected blob"),this.entryBlob=this.segments[0],this.segments.length=0,this.overheadBytes+=9,this.backBranchTargets&&(this.overheadBytes+=20,this.overheadBytes+=this.backBranchTargets.length),this.firstOpcodeIp}appendBlob(){this.builder.current.size!==this.lastSegmentEnd&&(this.segments.push({type:"blob",ip:this.lastSegmentStartIp,start:this.lastSegmentEnd,length:this.builder.current.size-this.lastSegmentEnd}),this.lastSegmentStartIp=this.ip,this.lastSegmentEnd=this.builder.current.size,this.overheadBytes+=2);}startBranchBlock(e,t){this.appendBlob(),this.segments.push({type:"branch-block-header",ip:e,isBackBranchTarget:t}),this.overheadBytes+=1;}branch(e,t,n){t&&this.observedBackBranchTargets.add(e),this.appendBlob(),this.segments.push({type:"branch",from:this.ip,target:e,isBackward:t,branchType:n}),this.overheadBytes+=4,t&&(this.overheadBytes+=4);}jumpTable(e,t){this.appendBlob(),this.segments.push({type:"jump-table",from:this.ip,targets:e,fallthrough:t}),this.overheadBytes+=4,this.overheadBytes+=e.length,this.overheadBytes+=24;}emitBlob(e,t){const n=t.subarray(e.start,e.start+e.length);this.builder.appendBytes(n);}generate(){this.appendBlob();const e=this.builder.endFunction(false);this.builder._push(),this.builder.base=this.base,this.emitBlob(this.entryBlob,e),this.backBranchTargets&&this.builder.block(64,3);for(let e=0;e<this.segments.length;e++){const t=this.segments[e];"branch-block-header"===t.type&&this.blockStack.push(t.ip);}this.blockStack.sort(((e,t)=>e-t));for(let e=0;e<this.blockStack.length;e++)this.builder.block(64);if(this.backBranchTargets){this.backDispatchOffsets.length=0;for(let e=0;e<this.backBranchTargets.length;e++){const t=2*this.backBranchTargets[e]+this.startOfBody;this.blockStack.indexOf(t)<0||this.observedBackBranchTargets.has(t)&&(this.dispatchTable.set(t,this.backDispatchOffsets.length+1),this.backDispatchOffsets.push(t));}if(0===this.backDispatchOffsets.length)this.trace>0&&Ve$1("No back branch targets were reachable after filtering");else if(1===this.backDispatchOffsets.length)this.trace>0&&(this.backDispatchOffsets[0]===this.entryIp?Ve$1(`Exactly one back dispatch offset and it was the entry point 0x${this.entryIp.toString(16)}`):Ve$1(`Exactly one back dispatch offset and it was 0x${this.backDispatchOffsets[0].toString(16)}`)),this.builder.local("disp"),this.builder.appendU8(13),this.builder.appendULeb(this.blockStack.indexOf(this.backDispatchOffsets[0]));else {this.trace>0&&Ve$1(`${this.backDispatchOffsets.length} back branch offsets after filtering.`),this.builder.block(64),this.builder.block(64),this.builder.local("disp"),this.builder.appendU8(14),this.builder.appendULeb(this.backDispatchOffsets.length+1),this.builder.appendULeb(1);for(let e=0;e<this.backDispatchOffsets.length;e++)this.builder.appendULeb(this.blockStack.indexOf(this.backDispatchOffsets[e])+2);this.builder.appendULeb(0),this.builder.endBlock(),this.builder.appendU8(0),this.builder.endBlock();}this.backDispatchOffsets.length>0&&this.blockStack.push(0);}this.trace>1&&Ve$1(`blockStack=${this.blockStack}`);for(let t=0;t<this.segments.length;t++){const n=this.segments[t];switch(n.type){case "blob":this.emitBlob(n,e);break;case "branch-block-header":{const e=this.blockStack.indexOf(n.ip);0!==e&&mt(false,`expected ${n.ip} on top of blockStack but found it at index ${e}, top is ${this.blockStack[0]}`),this.builder.endBlock(),this.blockStack.shift();break}case "jump-table":{const e=1;this.builder.appendU8(14),this.builder.appendULeb(n.targets.length);for(const t of n.targets){const n=this.blockStack.indexOf(t);n>=0?(da(13,1),this.builder.appendULeb(n+e)):(da(14,1),this.trace>0&&Ve$1(`Switch target ${t} not found in block stack ${this.blockStack}`),this.builder.appendULeb(0));}const t=this.blockStack.indexOf(n.fallthrough);t>=0?(da(13,1),this.builder.appendULeb(t+e)):(da(14,1),this.trace>0&&Ve$1(`Switch fallthrough ${n.fallthrough} not found in block stack ${this.blockStack}`),this.builder.appendULeb(0)),this.builder.appendU8(0);break}case "branch":{const e=n.isBackward?0:n.target;let t,r=this.blockStack.indexOf(e),o=false;if(n.isBackward&&(this.dispatchTable.has(n.target)?(t=this.dispatchTable.get(n.target),this.trace>1&&Ve$1(`backward br from ${n.from.toString(16)} to ${n.target.toString(16)}: disp=${t}`),o=true):(this.trace>0&&Ve$1(`br from ${n.from.toString(16)} to ${n.target.toString(16)} failed: back branch target not in dispatch table`),r=-1)),r>=0||o){let e=0;switch(n.branchType){case 2:this.builder,n.from,void 0!==t&&(this.builder.i32_const(t),this.builder.local("disp",33)),this.builder.appendU8(12);break;case 3:this.builder.block(64,4),this.builder,n.from,void 0!==t&&(this.builder.i32_const(t),this.builder.local("disp",33)),this.builder.appendU8(12),e=1;break;case 0:void 0!==t&&(this.builder.i32_const(t),this.builder.local("disp",33)),this.builder.appendU8(12);break;case 1:void 0!==t?(this.builder.block(64,4),this.builder.i32_const(t),this.builder.local("disp",33),e=1,this.builder.appendU8(12)):this.builder.appendU8(13);break;default:throw new Error("Unimplemented branch type")}this.builder.appendULeb(e+r),e&&this.builder.endBlock(),this.trace>1&&Ve$1(`br from ${n.from.toString(16)} to ${n.target.toString(16)} breaking out ${e+r+1} level(s)`);}else {if(this.trace>0){const e=this.base;n.target>=e&&n.target<this.exitIp?Ve$1(`br from ${n.from.toString(16)} to ${n.target.toString(16)} failed (inside of trace!)`):this.trace>1&&Ve$1(`br from ${n.from.toString(16)} to ${n.target.toString(16)} failed (outside of trace 0x${e.toString(16)} - 0x${this.exitIp.toString(16)})`);}const e=1===n.branchType||3===n.branchType;e&&this.builder.block(64,4),Ws(this.builder,n.target,4),e&&this.builder.endBlock();}break}default:throw new Error("unreachable")}}return this.backBranchTargets&&(this.blockStack.length<=1||mt(false,"expected one or zero entries in the block stack at the end"),this.blockStack.length&&this.blockStack.shift(),this.builder.endBlock()),0!==this.blockStack.length&&mt(false,`expected block stack to be empty at end of function but it was ${this.blockStack}`),this.builder.ip_const(this.exitIp),this.builder.appendU8(15),this.builder.appendU8(11),this.builder._pop(false)}}let Ps;const Ms={},zs=globalThis.performance&&globalThis.performance.now?globalThis.performance.now.bind(globalThis.performance):Date.now;function Vs(e,t,n){let r;switch(n){case 633:r="prof_enter";break;case 634:r="prof_samplepoint";break;case 635:case 636:r="prof_leave";break;default:throw new Error(`Unimplemented profiler event ${n}`)}e.local("frame"),e.i32_const(t),e.callImport(r);}function Ws(e,t,n){e.ip_const(t),e.options.countBailouts&&(e.i32_const(e.traceIndex),e.i32_const(n),e.callImport("bailout")),e.appendU8(15);}function Hs(e,t,n,r){e.local("cinfo"),e.block(64,4),e.local("cinfo"),e.local("disp"),e.appendU8(54),e.appendMemarg(ea(19),0),n<=e.options.monitoringLongDistance+2&&(e.local("cinfo"),e.i32_const(n),e.appendU8(54),e.appendMemarg(ea(20),0)),e.endBlock(),e.ip_const(t),e.options.countBailouts&&(e.i32_const(e.traceIndex),e.i32_const(r),e.callImport("bailout")),e.appendU8(15);}function qs(){if(Ps||(Ps=ct$1.getWasmIndirectFunctionTable()),!Ps)throw new Error("Module did not export the indirect function table");return Ps}function Gs(e,t){t||mt(false,"Attempting to set null function into table");const n=o$1.mono_jiterp_allocate_table_entry(e);return n>0&&qs().set(n,t),n}function Js(e,t,n,r,o){if(r<=0)return o&&e.appendU8(26),true;if(r>=Rs)return  false;const s=o?"memop_dest":"pLocals";o&&e.local(s,33);let a=o?0:t;if(e.options.enableSimd){const t=16;for(;r>=t;)e.local(s),e.v128_const(0),e.appendSimd(11),e.appendMemarg(a,0),a+=t,r-=t;}for(;r>=8;)e.local(s),e.i52_const(0),e.appendU8(55),e.appendMemarg(a,0),a+=8,r-=8;for(;r>=1;){e.local(s),e.i32_const(0);let t=r%4;switch(t){case 0:t=4,e.appendU8(54);break;case 1:e.appendU8(58);break;case 3:case 2:t=2,e.appendU8(59);}e.appendMemarg(a,0),a+=t,r-=t;}return  true}function Xs(e,t,n){Js(e,0,0,n,true)||(e.i32_const(t),e.i32_const(n),e.appendU8(252),e.appendU8(11),e.appendU8(0));}function Qs(e,t,n,r,o,s,a){if(r<=0)return o&&(e.appendU8(26),e.appendU8(26)),true;if(r>=Bs)return  false;o?(s=s||"memop_dest",a=a||"memop_src",e.local(a,33),e.local(s,33)):s&&a||(s=a="pLocals");let i=o?0:t,c=o?0:n;if(e.options.enableSimd){const t=16;for(;r>=t;)e.local(s),e.local(a),e.appendSimd(0,true),e.appendMemarg(c,0),e.appendSimd(11),e.appendMemarg(i,0),i+=t,c+=t,r-=t;}for(;r>=8;)e.local(s),e.local(a),e.appendU8(41),e.appendMemarg(c,0),e.appendU8(55),e.appendMemarg(i,0),i+=8,c+=8,r-=8;for(;r>=1;){let t,n,o=r%4;switch(o){case 0:o=4,t=40,n=54;break;default:case 1:o=1,t=44,n=58;break;case 3:case 2:o=2,t=46,n=59;}e.local(s),e.local(a),e.appendU8(t),e.appendMemarg(c,0),e.appendU8(n),e.appendMemarg(i,0),c+=o,i+=o,r-=o;}return  true}function Ys(e,t){return Qs(e,0,0,t,true)||(e.i32_const(t),e.appendU8(252),e.appendU8(10),e.appendU8(0),e.appendU8(0)),true}function Zs(){const e=da(5,1);e>=Ls&&(Ve$1(`Disabling jiterpreter after ${e} failures`),pa({enableTraces:false,enableInterpEntry:false,enableJitCall:false}));}const Ks={};function ea(e){const t=Ks[e];return void 0===t?Ks[e]=o$1.mono_jiterp_get_member_offset(e):t}function ta(e){const t=Ke$1.wasmExports[e];if("function"!=typeof t)throw new Error(`raw cwrap ${e} not found`);return t}const na={};function ra(e){let t=na[e];return "number"!=typeof t&&(t=na[e]=o$1.mono_jiterp_get_opcode_value_table_entry(e)),t}function oa(e,t){return [e,e,t]}let sa;function aa(){if(!o$1.mono_wasm_is_zero_page_reserved())return  false;if(true===sa)return  false;const e=te$1();for(let t=0;t<8;t++)if(0!==e[t])return  false===sa&&He$1(`Zero page optimizations are enabled but garbage appeared in memory at address ${4*t}: ${e[t]}`),sa=true,false;return sa=false,true}const ia={enableTraces:"jiterpreter-traces-enabled",enableInterpEntry:"jiterpreter-interp-entry-enabled",enableJitCall:"jiterpreter-jit-call-enabled",enableBackwardBranches:"jiterpreter-backward-branch-entries-enabled",enableCallResume:"jiterpreter-call-resume-enabled",enableWasmEh:"jiterpreter-wasm-eh-enabled",enableSimd:"jiterpreter-simd-enabled",enableAtomics:"jiterpreter-atomics-enabled",zeroPageOptimization:"jiterpreter-zero-page-optimization",cprop:"jiterpreter-constant-propagation",enableStats:"jiterpreter-stats-enabled",disableHeuristic:"jiterpreter-disable-heuristic",estimateHeat:"jiterpreter-estimate-heat",countBailouts:"jiterpreter-count-bailouts",dumpTraces:"jiterpreter-dump-traces",useConstants:"jiterpreter-use-constants",eliminateNullChecks:"jiterpreter-eliminate-null-checks",noExitBackwardBranches:"jiterpreter-backward-branches-enabled",directJitCalls:"jiterpreter-direct-jit-calls",minimumTraceValue:"jiterpreter-minimum-trace-value",minimumTraceHitCount:"jiterpreter-minimum-trace-hit-count",monitoringPeriod:"jiterpreter-trace-monitoring-period",monitoringShortDistance:"jiterpreter-trace-monitoring-short-distance",monitoringLongDistance:"jiterpreter-trace-monitoring-long-distance",monitoringMaxAveragePenalty:"jiterpreter-trace-monitoring-max-average-penalty",backBranchBoost:"jiterpreter-back-branch-boost",jitCallHitCount:"jiterpreter-jit-call-hit-count",jitCallFlushThreshold:"jiterpreter-jit-call-queue-flush-threshold",interpEntryHitCount:"jiterpreter-interp-entry-hit-count",interpEntryFlushThreshold:"jiterpreter-interp-entry-queue-flush-threshold",wasmBytesLimit:"jiterpreter-wasm-bytes-limit",tableSize:"jiterpreter-table-size",aotTableSize:"jiterpreter-aot-table-size",maxModuleSize:"jiterpreter-max-module-size",maxSwitchSize:"jiterpreter-max-switch-size"};let ca=-1,la={};function pa(e){for(const t in e){const n=ia[t];if(!n){He$1(`Unrecognized jiterpreter option: ${t}`);continue}const r=e[t];"boolean"==typeof r?o$1.mono_jiterp_parse_option((r?"--":"--no-")+n):"number"==typeof r?o$1.mono_jiterp_parse_option(`--${n}=${r}`):He$1(`Jiterpreter option must be a boolean or a number but was ${typeof r} '${r}'`);}}function ua(e){return o$1.mono_jiterp_get_counter(e)}function da(e,t){return o$1.mono_jiterp_modify_counter(e,t)}function fa(){const e=o$1.mono_jiterp_get_options_version();return e!==ca&&(function(){la={};for(const e in ia){const t=o$1.mono_jiterp_get_option_as_int(ia[e]);t!==Ns?la[e]=t:Ve$1(`Failed to retrieve value of option ${ia[e]}`);}}(),ca=e),la}function _a(e,t,n,r){const s=qs(),a=t,i=a+n-1;return i<s.length||mt(false,`Last index out of range: ${i} >= ${s.length}`),s.set(a,r),o$1.mono_jiterp_initialize_table(e,a,i),t+n}let ma=false;const ha=["Unknown","InterpreterTiering","NullCheck","VtableNotInitialized","Branch","BackwardBranch","ConditionalBranch","ConditionalBackwardBranch","ComplexBranch","ArrayLoadFailed","ArrayStoreFailed","StringOperationFailed","DivideByZero","Overflow","Return","Call","Throw","AllocFailed","SpanOperationFailed","CastFailed","SafepointBranchTaken","UnboxFailed","CallDelegate","Debugging","Icall","UnexpectedRetIp","LeaveCheck","SwitchSize","SwitchTarget"],ga={2:["V128_I1_NEGATION","V128_I2_NEGATION","V128_I4_NEGATION","V128_ONES_COMPLEMENT","V128_U2_WIDEN_LOWER","V128_U2_WIDEN_UPPER","V128_I1_CREATE_SCALAR","V128_I2_CREATE_SCALAR","V128_I4_CREATE_SCALAR","V128_I8_CREATE_SCALAR","V128_I1_EXTRACT_MSB","V128_I2_EXTRACT_MSB","V128_I4_EXTRACT_MSB","V128_I8_EXTRACT_MSB","V128_I1_CREATE","V128_I2_CREATE","V128_I4_CREATE","V128_I8_CREATE","SplatX1","SplatX2","SplatX4","SplatX8","NegateD1","NegateD2","NegateD4","NegateD8","NegateR4","NegateR8","SqrtR4","SqrtR8","CeilingR4","CeilingR8","FloorR4","FloorR8","TruncateR4","TruncateR8","RoundToNearestR4","RoundToNearestR8","NotANY","AnyTrueANY","AllTrueD1","AllTrueD2","AllTrueD4","AllTrueD8","PopCountU1","BitmaskD1","BitmaskD2","BitmaskD4","BitmaskD8","AddPairwiseWideningI1","AddPairwiseWideningU1","AddPairwiseWideningI2","AddPairwiseWideningU2","AbsI1","AbsI2","AbsI4","AbsI8","AbsR4","AbsR8","ConvertToSingleI4","ConvertToSingleU4","ConvertToSingleR8","ConvertToDoubleLowerI4","ConvertToDoubleLowerU4","ConvertToDoubleLowerR4","ConvertToInt32SaturateR4","ConvertToUInt32SaturateR4","ConvertToInt32SaturateR8","ConvertToUInt32SaturateR8","SignExtendWideningLowerD1","SignExtendWideningLowerD2","SignExtendWideningLowerD4","SignExtendWideningUpperD1","SignExtendWideningUpperD2","SignExtendWideningUpperD4","ZeroExtendWideningLowerD1","ZeroExtendWideningLowerD2","ZeroExtendWideningLowerD4","ZeroExtendWideningUpperD1","ZeroExtendWideningUpperD2","ZeroExtendWideningUpperD4","LoadVector128ANY","LoadScalarVector128X4","LoadScalarVector128X8","LoadScalarAndSplatVector128X1","LoadScalarAndSplatVector128X2","LoadScalarAndSplatVector128X4","LoadScalarAndSplatVector128X8","LoadWideningVector128I1","LoadWideningVector128U1","LoadWideningVector128I2","LoadWideningVector128U2","LoadWideningVector128I4","LoadWideningVector128U4"],3:["V128_I1_ADD","V128_I2_ADD","V128_I4_ADD","V128_R4_ADD","V128_I1_SUB","V128_I2_SUB","V128_I4_SUB","V128_R4_SUB","V128_BITWISE_AND","V128_BITWISE_OR","V128_BITWISE_EQUALITY","V128_BITWISE_INEQUALITY","V128_R4_FLOAT_EQUALITY","V128_R8_FLOAT_EQUALITY","V128_EXCLUSIVE_OR","V128_I1_MULTIPLY","V128_I2_MULTIPLY","V128_I4_MULTIPLY","V128_R4_MULTIPLY","V128_R4_DIVISION","V128_I1_LEFT_SHIFT","V128_I2_LEFT_SHIFT","V128_I4_LEFT_SHIFT","V128_I8_LEFT_SHIFT","V128_I1_RIGHT_SHIFT","V128_I2_RIGHT_SHIFT","V128_I4_RIGHT_SHIFT","V128_I1_URIGHT_SHIFT","V128_I2_URIGHT_SHIFT","V128_I4_URIGHT_SHIFT","V128_I8_URIGHT_SHIFT","V128_U1_NARROW","V128_U1_GREATER_THAN","V128_I1_LESS_THAN","V128_U1_LESS_THAN","V128_I2_LESS_THAN","V128_I1_EQUALS","V128_I2_EQUALS","V128_I4_EQUALS","V128_R4_EQUALS","V128_I8_EQUALS","V128_I1_EQUALS_ANY","V128_I2_EQUALS_ANY","V128_I4_EQUALS_ANY","V128_I8_EQUALS_ANY","V128_AND_NOT","V128_U2_LESS_THAN_EQUAL","V128_I1_SHUFFLE","V128_I2_SHUFFLE","V128_I4_SHUFFLE","V128_I8_SHUFFLE","ExtractScalarI1","ExtractScalarU1","ExtractScalarI2","ExtractScalarU2","ExtractScalarD4","ExtractScalarD8","ExtractScalarR4","ExtractScalarR8","SwizzleD1","AddD1","AddD2","AddD4","AddD8","AddR4","AddR8","SubtractD1","SubtractD2","SubtractD4","SubtractD8","SubtractR4","SubtractR8","MultiplyD2","MultiplyD4","MultiplyD8","MultiplyR4","MultiplyR8","DivideR4","DivideR8","DotI2","ShiftLeftD1","ShiftLeftD2","ShiftLeftD4","ShiftLeftD8","ShiftRightArithmeticD1","ShiftRightArithmeticD2","ShiftRightArithmeticD4","ShiftRightArithmeticD8","ShiftRightLogicalD1","ShiftRightLogicalD2","ShiftRightLogicalD4","ShiftRightLogicalD8","AndANY","AndNotANY","OrANY","XorANY","CompareEqualD1","CompareEqualD2","CompareEqualD4","CompareEqualD8","CompareEqualR4","CompareEqualR8","CompareNotEqualD1","CompareNotEqualD2","CompareNotEqualD4","CompareNotEqualD8","CompareNotEqualR4","CompareNotEqualR8","CompareLessThanI1","CompareLessThanU1","CompareLessThanI2","CompareLessThanU2","CompareLessThanI4","CompareLessThanU4","CompareLessThanI8","CompareLessThanR4","CompareLessThanR8","CompareLessThanOrEqualI1","CompareLessThanOrEqualU1","CompareLessThanOrEqualI2","CompareLessThanOrEqualU2","CompareLessThanOrEqualI4","CompareLessThanOrEqualU4","CompareLessThanOrEqualI8","CompareLessThanOrEqualR4","CompareLessThanOrEqualR8","CompareGreaterThanI1","CompareGreaterThanU1","CompareGreaterThanI2","CompareGreaterThanU2","CompareGreaterThanI4","CompareGreaterThanU4","CompareGreaterThanI8","CompareGreaterThanR4","CompareGreaterThanR8","CompareGreaterThanOrEqualI1","CompareGreaterThanOrEqualU1","CompareGreaterThanOrEqualI2","CompareGreaterThanOrEqualU2","CompareGreaterThanOrEqualI4","CompareGreaterThanOrEqualU4","CompareGreaterThanOrEqualI8","CompareGreaterThanOrEqualR4","CompareGreaterThanOrEqualR8","ConvertNarrowingSaturateSignedI2","ConvertNarrowingSaturateSignedI4","ConvertNarrowingSaturateUnsignedI2","ConvertNarrowingSaturateUnsignedI4","MultiplyWideningLowerI1","MultiplyWideningLowerI2","MultiplyWideningLowerI4","MultiplyWideningLowerU1","MultiplyWideningLowerU2","MultiplyWideningLowerU4","MultiplyWideningUpperI1","MultiplyWideningUpperI2","MultiplyWideningUpperI4","MultiplyWideningUpperU1","MultiplyWideningUpperU2","MultiplyWideningUpperU4","AddSaturateI1","AddSaturateU1","AddSaturateI2","AddSaturateU2","SubtractSaturateI1","SubtractSaturateU1","SubtractSaturateI2","SubtractSaturateU2","MultiplyRoundedSaturateQ15I2","MinI1","MinI2","MinI4","MinU1","MinU2","MinU4","MaxI1","MaxI2","MaxI4","MaxU1","MaxU2","MaxU4","AverageRoundedU1","AverageRoundedU2","MinR4","MinR8","MaxR4","MaxR8","PseudoMinR4","PseudoMinR8","PseudoMaxR4","PseudoMaxR8","StoreANY"],4:["V128_CONDITIONAL_SELECT","ReplaceScalarD1","ReplaceScalarD2","ReplaceScalarD4","ReplaceScalarD8","ReplaceScalarR4","ReplaceScalarR8","ShuffleD1","BitwiseSelectANY","LoadScalarAndInsertX1","LoadScalarAndInsertX2","LoadScalarAndInsertX4","LoadScalarAndInsertX8","StoreSelectedScalarX1","StoreSelectedScalarX2","StoreSelectedScalarX4","StoreSelectedScalarX8"]},ba={13:[65,0],14:[65,1]},ya={456:168,462:174,457:170,463:176},wa={508:[69,40,54],428:[106,40,54],430:[107,40,54],432:[107,40,54],436:[115,40,54],429:[124,41,55],431:[125,41,55],433:[125,41,55],437:[133,41,55],511:[106,40,54],515:[108,40,54],513:[124,41,55],517:[126,41,55],434:[140,42,56],435:[154,43,57],464:[178,40,56],467:[183,40,57],438:[184,40,57],465:[180,41,56],468:[185,41,57],439:[186,41,57],469:[187,42,57],466:[182,43,56],460:[1,52,55],461:[1,53,55],444:[113,40,54],452:[113,40,54],440:[117,40,54],448:[117,40,54],445:[113,41,54],453:[113,41,54],441:[117,41,54],449:[117,41,54],525:[116,40,54],526:[134,41,55],527:[117,40,54],528:[135,41,55],523:[118,40,54],524:[136,41,55],640:[119,40,54],641:[137,41,55],642:[120,40,54],643:[138,41,55],644:[103,40,54],646:[104,40,54],648:[105,40,54],645:[121,41,55],647:[122,41,55],649:[123,41,55],512:[106,40,54],516:[108,40,54],514:[124,41,55],518:[126,41,55],519:[113,40,54],520:[113,40,54],521:[114,40,54],522:[114,40,54]},ka={394:187,395:1,398:187,399:1,402:187,403:1,406:187,407:1,412:187,413:1,416:187,417:1,426:187,427:1,420:187,421:1,65536:187,65537:187,65535:187,65539:1,65540:1,65538:1},Sa={344:[106,40,54],362:[106,40,54],364:[106,40,54],348:[107,40,54],352:[108,40,54],366:[108,40,54],368:[108,40,54],356:[109,40,54],360:[110,40,54],380:[111,40,54],384:[112,40,54],374:[113,40,54],376:[114,40,54],378:[115,40,54],388:[116,40,54],390:[117,40,54],386:[118,40,54],345:[124,41,55],349:[125,41,55],353:[126,41,55],357:[127,41,55],381:[129,41,55],361:[128,41,55],385:[130,41,55],375:[131,41,55],377:[132,41,55],379:[133,41,55],389:[134,41,55],391:[135,41,55],387:[136,41,55],346:[146,42,56],350:[147,42,56],354:[148,42,56],358:[149,42,56],347:[160,43,57],351:[161,43,57],355:[162,43,57],359:[163,43,57],392:[70,40,54],396:[71,40,54],414:[72,40,54],400:[74,40,54],418:[76,40,54],404:[78,40,54],424:[73,40,54],410:[75,40,54],422:[77,40,54],408:[79,40,54],393:[81,41,54],397:[82,41,54],415:[83,41,54],401:[85,41,54],419:[87,41,54],405:[89,41,54],425:[84,41,54],411:[86,41,54],423:[88,41,54],409:[90,41,54]},va={187:392,207:396,195:400,215:410,199:414,223:424,191:404,211:408,203:418,219:422,231:[392,false,true],241:[396,false,true],235:[400,false,true],245:[410,false,true],237:[414,false,true],249:[424,false,true],233:[404,false,true],243:[408,false,true],239:[418,false,true],247:[422,false,true],251:[392,65,true],261:[396,65,true],255:[400,65,true],265:[410,65,true],257:[414,65,true],269:[424,65,true],253:[404,65,true],263:[408,65,true],259:[418,65,true],267:[422,65,true],188:393,208:397,196:401,216:411,200:415,224:425,192:405,212:409,204:419,220:423,252:[393,66,true],256:[401,66,true],266:[411,66,true],258:[415,66,true],270:[425,66,true],254:[405,66,true],264:[409,66,true],260:[419,66,true],268:[423,66,true],189:394,209:65535,197:402,217:412,201:416,225:426,193:406,213:65536,205:420,221:65537,190:395,210:65538,198:403,218:413,202:417,226:427,194:407,214:65539,206:421,222:65540},Ua={599:[true,false,159],626:[true,true,145],586:[true,false,155],613:[true,true,141],592:[true,false,156],619:[true,true,142],603:[true,false,153],630:[true,true,139],581:[true,false,"acos"],608:[true,true,"acosf"],582:[true,false,"acosh"],609:[true,true,"acoshf"],587:[true,false,"cos"],614:[true,true,"cosf"],579:[true,false,"asin"],606:[true,true,"asinf"],580:[true,false,"asinh"],607:[true,true,"asinhf"],598:[true,false,"sin"],625:[true,true,"sinf"],583:[true,false,"atan"],610:[true,true,"atanf"],584:[true,false,"atanh"],611:[true,true,"atanhf"],601:[true,false,"tan"],628:[true,true,"tanf"],588:[true,false,"cbrt"],615:[true,true,"cbrtf"],590:[true,false,"exp"],617:[true,true,"expf"],593:[true,false,"log"],620:[true,true,"logf"],594:[true,false,"log2"],621:[true,true,"log2f"],595:[true,false,"log10"],622:[true,true,"log10f"],604:[false,false,164],631:[false,true,150],605:[false,false,165],632:[false,true,151],585:[false,false,"atan2"],612:[false,true,"atan2f"],596:[false,false,"pow"],623:[false,true,"powf"],383:[false,false,"fmod"],382:[false,true,"fmodf"]},Ta={560:[67,0,0],561:[67,192,0],562:[68,0,1],563:[68,193,1],564:[65,0,2],565:[66,0,3]},Ea={566:[74,0,0],567:[74,192,0],568:[75,0,1],569:[75,193,1],570:[72,0,2],571:[73,0,3]},xa={653:1,654:2,655:4,656:8},Ia={653:44,654:46,655:40,656:41},Aa={653:58,654:59,655:54,656:55},ja=new Set([20,21,22,23,24,25,26,27,28,29,30,80,81,82,83,84,85,86,87,88,89,90,91]),$a={51:[16,54],52:[16,54],53:[8,54],54:[8,54],55:[4,54],57:[4,56],56:[2,55],58:[2,57]},La={1:[16,40],2:[8,40],3:[4,40],5:[4,42],4:[2,41],6:[2,43]},Ra=new Set([81,84,85,86,87,82,83,88,89,90,91,92,93]),Ba={13:[16],14:[8],15:[4],16:[2]},Na={10:100,11:132,12:164,13:196,45:100,46:132,47:164,48:196},Oa={6:[44,23],7:[46,26],8:[40,28],9:[41,30]};function Ca(e,t){return O$1(e+2*t)}function Da(e,t){return z$1(e+2*t)}function Fa(e,t){return P$1(e+2*t)}function Pa(e,t){return F$1(e+2*t)}function Ma(e){return P$1(e+ea(4))}function za(e,t){const n=P$1(Ma(e)+ea(5));return P$1(n+t*gc)}function Va(e,t){const n=P$1(Ma(e)+ea(12));return P$1(n+t*gc)}function Wa(e,t,n){if(!n)return  false;for(let r=0;r<n.length;r++)if(2*n[r]+t===e)return  true;return  false}const Ha=new Map;function qa(e,t){if(!ci(e,t))return Ha.get(t)}function Ga(e,t){const n=qa(e,t);if(void 0!==n)switch(n.type){case "i32":case "v128":return n.value}}function Ja(e,t){try{let n=o$1.mono_jiterp_get_opcode_info(t,1);return 271===t&&(n=4+2*Fa(e,2)),n}catch(n){throw He$1(`Found invalid opcode ${t} at ip ${e}`),n}}const Xa=new Map;let Qa=-1;function Ya(){Qa=-1,Xa.clear(),Ha.clear();}function Za(e){Qa===e&&(Qa=-1),Xa.delete(e),Ha.delete(e);}function Ka(e,t){for(let n=0;n<t;n+=1)Za(e+n);}function ei(e,t,n){e.cfg.startBranchBlock(t,n);}function ti(e,t,n){let r=0;switch(e%16==0?r=4:e%8==0?r=3:e%4==0?r=2:e%2==0&&(r=1),t){case 253:r=0===n||11===n?Math.min(r,4):0;break;case 41:case 43:case 55:case 57:r=Math.min(r,3);break;case 52:case 53:case 62:case 40:case 42:case 54:case 56:r=Math.min(r,2);break;case 50:case 51:case 46:case 47:case 61:case 59:r=Math.min(r,1);break;default:r=0;}return r}function ni(e,t,n,r,o){if(e.options.cprop&&40===n){const n=qa(e,t);if(n)switch(n.type){case "i32":return !(o&&0===n.value||(r||e.i32_const(n.value),0));case "ldloca":return r||si(e,n.offset,0),true}}return  false}function ri(e,t,n,r){if(ni(e,t,n,false))return;if(e.local("pLocals"),n>=40||mt(false,`Expected load opcode but got ${n}`),e.appendU8(n),void 0!==r)e.appendULeb(r);else if(253===n)throw new Error("PREFIX_simd ldloc without a simdOpcode");const o=ti(t,n,r);e.appendMemarg(t,o);}function oi(e,t,n,r){n>=54||mt(false,`Expected store opcode but got ${n}`),e.appendU8(n),void 0!==r&&e.appendULeb(r);const o=ti(t,n,r);e.appendMemarg(t,o),Za(t),void 0!==r&&Za(t+8);}function si(e,t,n){n>0&&Ka(t,n),e.lea("pLocals",t);}function ai(e,t,n,r){Ka(t,r),Js(e,t,0,r,false)||(si(e,t,r),Xs(e,n,r));}function ii(e,t,n,r){if(Ka(t,r),Qs(e,t,n,r,false))return  true;si(e,t,r),si(e,n,0),Ys(e,r);}function ci(e,t){return 0!==o$1.mono_jiterp_is_imethod_var_address_taken(Ma(e.frame),t)}function li(e,t,n,r){if(e.allowNullCheckOptimization&&Xa.has(t)&&!ci(e,t))return da(7,1),void(Qa===t?r&&e.local("cknull_ptr"):(ri(e,t,40),e.local("cknull_ptr",r?34:33),Qa=t));ri(e,t,40),e.local("cknull_ptr",34),e.appendU8(69),e.block(64,4),Ws(e,n,2),e.endBlock(),r&&e.local("cknull_ptr"),e.allowNullCheckOptimization&&!ci(e,t)?(Xa.set(t,n),Qa=t):Qa=-1;}function pi(e,t,n){let r,s=54;const a=ba[n];if(a)e.local("pLocals"),e.appendU8(a[0]),r=a[1],e.appendLeb(r);else switch(n){case 15:e.local("pLocals"),r=Da(t,2),e.i32_const(r);break;case 16:e.local("pLocals"),r=Pa(t,2),e.i32_const(r);break;case 17:e.local("pLocals"),e.i52_const(0),s=55;break;case 19:e.local("pLocals"),e.appendU8(66),e.appendLebRef(t+4,true),s=55;break;case 18:e.local("pLocals"),e.i52_const(Da(t,2)),s=55;break;case 20:e.local("pLocals"),e.appendU8(67),e.appendF32(function(e,t){return n=e+2*t,o$1.mono_wasm_get_f32_unaligned(n);var n;}(t,2)),s=56;break;case 21:e.local("pLocals"),e.appendU8(68),e.appendF64(function(e,t){return n=e+2*t,o$1.mono_wasm_get_f64_unaligned(n);var n;}(t,2)),s=57;break;default:return  false}e.appendU8(s);const i=Ca(t,1);return e.appendMemarg(i,2),Za(i),"number"==typeof r?Ha.set(i,{type:"i32",value:r}):Ha.delete(i),true}function ui(e,t,n){let r=40,o=54;switch(n){case 74:r=44;break;case 75:r=45;break;case 76:r=46;break;case 77:r=47;break;case 78:r=45,o=58;break;case 79:r=47,o=59;break;case 80:break;case 81:r=41,o=55;break;case 82:{const n=Ca(t,3);return ii(e,Ca(t,1),Ca(t,2),n),true}case 83:return ii(e,Ca(t,1),Ca(t,2),8),ii(e,Ca(t,3),Ca(t,4),8),true;case 84:return ii(e,Ca(t,1),Ca(t,2),8),ii(e,Ca(t,3),Ca(t,4),8),ii(e,Ca(t,5),Ca(t,6),8),true;case 85:return ii(e,Ca(t,1),Ca(t,2),8),ii(e,Ca(t,3),Ca(t,4),8),ii(e,Ca(t,5),Ca(t,6),8),ii(e,Ca(t,7),Ca(t,8),8),true;default:return  false}return e.local("pLocals"),ri(e,Ca(t,2),r),oi(e,Ca(t,1),o),true}function di(e,t,n,r){const o=r>=23&&r<=36||r>=50&&r<=60,s=Ca(n,o?2:1),a=Ca(n,3),i=Ca(n,o?1:2),c=e.allowNullCheckOptimization&&Xa.has(s)&&!ci(e,s);36!==r&&45!==r&&li(e,s,n,false);let l=54,p=40;switch(r){case 23:p=44;break;case 24:p=45;break;case 25:p=46;break;case 26:p=47;break;case 31:case 41:case 27:break;case 43:case 29:p=42,l=56;break;case 44:case 30:p=43,l=57;break;case 37:case 38:l=58;break;case 39:case 40:l=59;break;case 28:case 42:p=41,l=55;break;case 45:return c||e.block(),e.local("pLocals"),e.i32_const(a),e.i32_const(s),e.i32_const(i),e.callImport("stfld_o"),c?(e.appendU8(26),da(7,1)):(e.appendU8(13),e.appendULeb(0),Ws(e,n,2),e.endBlock()),true;case 32:{const t=Ca(n,4);return si(e,i,t),e.local("cknull_ptr"),0!==a&&(e.i32_const(a),e.appendU8(106)),Ys(e,t),true}case 46:{const r=za(t,Ca(n,4));return e.local("cknull_ptr"),0!==a&&(e.i32_const(a),e.appendU8(106)),si(e,i,0),e.ptr_const(r),e.callImport("value_copy"),true}case 47:{const t=Ca(n,4);return e.local("cknull_ptr"),0!==a&&(e.i32_const(a),e.appendU8(106)),si(e,i,0),Ys(e,t),true}case 36:case 35:return e.local("pLocals"),ri(e,s,40),0!==a&&(e.i32_const(a),e.appendU8(106)),oi(e,i,l),true;default:return  false}return o&&e.local("pLocals"),e.local("cknull_ptr"),o?(e.appendU8(p),e.appendMemarg(a,0),oi(e,i,l),true):(ri(e,i,p),e.appendU8(l),e.appendMemarg(a,0),true)}function fi(e,t,n,r){const o=r>=23&&r<=36||r>=50&&r<=60,s=Ca(n,1),a=za(t,Ca(n,2)),i=za(t,Ca(n,3));!function(e,t,n){e.block(),e.ptr_const(t),e.appendU8(45),e.appendMemarg(ea(0),0),e.appendU8(13),e.appendULeb(0),Ws(e,n,3),e.endBlock();}(e,a,n);let c=54,l=40;switch(r){case 50:l=44;break;case 51:l=45;break;case 52:l=46;break;case 53:l=47;break;case 58:case 65:case 54:break;case 67:case 56:l=42,c=56;break;case 68:case 57:l=43,c=57;break;case 61:case 62:c=58;break;case 63:case 64:c=59;break;case 55:case 66:l=41,c=55;break;case 69:return e.ptr_const(i),si(e,s,0),e.callImport("copy_ptr"),true;case 59:{const t=Ca(n,4);return si(e,s,t),e.ptr_const(i),Ys(e,t),true}case 72:return e.local("pLocals"),e.ptr_const(i),oi(e,s,c),true;default:return  false}return o?(e.local("pLocals"),e.ptr_const(i),e.appendU8(l),e.appendMemarg(0,0),oi(e,s,c),true):(e.ptr_const(i),ri(e,s,l),e.appendU8(c),e.appendMemarg(0,0),true)}function _i(e,t,n){let r,o,s,a,i="math_lhs32",c="math_rhs32",l=false;const p=ka[n];if(p){e.local("pLocals");const r=1==p;return ri(e,Ca(t,2),r?43:42),r||e.appendU8(p),ri(e,Ca(t,3),r?43:42),r||e.appendU8(p),e.i32_const(n),e.callImport("relop_fp"),oi(e,Ca(t,1),54),true}switch(n){case 382:case 383:return wi(e,t,n);default:if(a=Sa[n],!a)return  false;a.length>3?(r=a[1],o=a[2],s=a[3]):(r=o=a[1],s=a[2]);}switch(n){case 356:case 357:case 360:case 361:case 380:case 381:case 384:case 385:{const s=361===n||385===n||357===n||381===n;i=s?"math_lhs64":"math_lhs32",c=s?"math_rhs64":"math_rhs32",e.block(),ri(e,Ca(t,2),r),e.local(i,33),ri(e,Ca(t,3),o),e.local(c,34),l=true,s&&(e.appendU8(80),e.appendU8(69)),e.appendU8(13),e.appendULeb(0),Ws(e,t,12),e.endBlock(),356!==n&&380!==n&&357!==n&&381!==n||(e.block(),e.local(c),s?e.i52_const(-1):e.i32_const(-1),e.appendU8(s?82:71),e.appendU8(13),e.appendULeb(0),e.local(i),e.appendU8(s?66:65),e.appendBoundaryValue(s?64:32,-1),e.appendU8(s?82:71),e.appendU8(13),e.appendULeb(0),Ws(e,t,13),e.endBlock());break}case 362:case 364:case 366:case 368:ri(e,Ca(t,2),r),e.local(i,34),ri(e,Ca(t,3),o),e.local(c,34),e.i32_const(n),e.callImport(364===n||368===n?"ckovr_u4":"ckovr_i4"),e.block(64,4),Ws(e,t,13),e.endBlock(),l=true;}return e.local("pLocals"),l?(e.local(i),e.local(c)):(ri(e,Ca(t,2),r),ri(e,Ca(t,3),o)),e.appendU8(a[0]),oi(e,Ca(t,1),s),true}function mi(e,t,n){const r=wa[n];if(!r)return  false;const o=r[1],s=r[2];switch((n<472||n>507)&&e.local("pLocals"),n){case 428:case 430:ri(e,Ca(t,2),o),e.i32_const(1);break;case 432:e.i32_const(0),ri(e,Ca(t,2),o);break;case 436:ri(e,Ca(t,2),o),e.i32_const(-1);break;case 444:case 445:ri(e,Ca(t,2),o),41===o&&e.appendU8(167),e.i32_const(255);break;case 452:case 453:ri(e,Ca(t,2),o),41===o&&e.appendU8(167),e.i32_const(65535);break;case 440:case 441:ri(e,Ca(t,2),o),41===o&&e.appendU8(167),e.i32_const(24),e.appendU8(116),e.i32_const(24);break;case 448:case 449:ri(e,Ca(t,2),o),41===o&&e.appendU8(167),e.i32_const(16),e.appendU8(116),e.i32_const(16);break;case 429:case 431:ri(e,Ca(t,2),o),e.i52_const(1);break;case 433:e.i52_const(0),ri(e,Ca(t,2),o);break;case 437:ri(e,Ca(t,2),o),e.i52_const(-1);break;case 511:case 515:case 519:case 521:case 525:case 527:case 523:case 640:case 642:ri(e,Ca(t,2),o),e.i32_const(Da(t,3));break;case 512:case 516:case 520:case 522:ri(e,Ca(t,2),o),e.i32_const(Pa(t,3));break;case 513:case 517:case 526:case 528:case 524:case 641:case 643:ri(e,Ca(t,2),o),e.i52_const(Da(t,3));break;case 514:case 518:ri(e,Ca(t,2),o),e.i52_const(Pa(t,3));break;default:ri(e,Ca(t,2),o);}return 1!==r[0]&&e.appendU8(r[0]),oi(e,Ca(t,1),s),true}function hi(e,t,n,r){const o=133===r?t+6:t+8,s=Va(n,O$1(o-2));e.local("pLocals"),e.ptr_const(o),e.appendU8(54),e.appendMemarg(s,0),e.callHandlerReturnAddresses.push(o);}function gi(e,t){const n=o$1.mono_jiterp_get_opcode_info(t,4),r=e+2+2*o$1.mono_jiterp_get_opcode_info(t,2);let s;switch(n){case 7:s=F$1(r);break;case 8:s=z$1(r);break;case 17:s=z$1(r+2);break;default:return}return s}function bi(e,t,n,r){const s=r>=227&&r<=270,a=gi(t,r);if("number"!=typeof a)return  false;switch(r){case 132:case 133:case 128:case 129:{const s=132===r||133===r,i=t+2*a;return a<=0?e.backBranchOffsets.indexOf(i)>=0?(e.backBranchTraceLevel>1&&Ve$1(`0x${t.toString(16)} performing backward branch to 0x${i.toString(16)}`),s&&hi(e,t,n,r),e.cfg.branch(i,true,0),da(9,1),true):(i<e.cfg.entryIp?(e.backBranchTraceLevel>1||e.cfg.trace>1)&&Ve$1(`0x${t.toString(16)} ${$s(r)} target 0x${i.toString(16)} before start of trace`):(e.backBranchTraceLevel>0||e.cfg.trace>0)&&Ve$1(`0x${t.toString(16)} ${$s(r)} target 0x${i.toString(16)} not found in list `+e.backBranchOffsets.map((e=>"0x"+e.toString(16))).join(", ")),o$1.mono_jiterp_boost_back_branch_target(i),Ws(e,i,5),da(10,1),true):(e.branchTargets.add(i),s&&hi(e,t,n,r),e.cfg.branch(i,false,0),true)}case 145:case 143:case 229:case 227:case 146:case 144:{const n=146===r||144===r;ri(e,Ca(t,1),n?41:40),143===r||227===r?e.appendU8(69):144===r?e.appendU8(80):146===r&&(e.appendU8(80),e.appendU8(69));break}default:if(void 0===va[r])throw new Error(`Unsupported relop branch opcode: ${$s(r)}`);if(4!==o$1.mono_jiterp_get_opcode_info(r,1))throw new Error(`Unsupported long branch opcode: ${$s(r)}`)}const i=t+2*a;return a<0?e.backBranchOffsets.indexOf(i)>=0?(e.backBranchTraceLevel>1&&Ve$1(`0x${t.toString(16)} performing conditional backward branch to 0x${i.toString(16)}`),e.cfg.branch(i,true,s?3:1),da(9,1)):(i<e.cfg.entryIp?(e.backBranchTraceLevel>1||e.cfg.trace>1)&&Ve$1(`0x${t.toString(16)} ${$s(r)} target 0x${i.toString(16)} before start of trace`):(e.backBranchTraceLevel>0||e.cfg.trace>0)&&Ve$1(`0x${t.toString(16)} ${$s(r)} target 0x${i.toString(16)} not found in list `+e.backBranchOffsets.map((e=>"0x"+e.toString(16))).join(", ")),o$1.mono_jiterp_boost_back_branch_target(i),e.block(64,4),Ws(e,i,5),e.endBlock(),da(10,1)):(e.branchTargets.add(i),e.cfg.branch(i,false,s?3:1)),true}function yi(e,t,n,r){const o=va[r];if(!o)return  false;const s=Array.isArray(o)?o[0]:o,a=Sa[s],i=ka[s];if(!a&&!i)return  false;const c=a?a[1]:1===i?43:42;return ri(e,Ca(t,1),c),a||1===i||e.appendU8(i),Array.isArray(o)&&o[1]?(e.appendU8(o[1]),e.appendLeb(Da(t,2))):ri(e,Ca(t,2),c),a||1==i||e.appendU8(i),a?e.appendU8(a[0]):(e.i32_const(s),e.callImport("relop_fp")),bi(e,t,n,r)}function wi(e,t,n){let r,o,s,a;const i=Ca(t,1),c=Ca(t,2),l=Ca(t,3),p=Ua[n];if(!p)return  false;if(r=p[0],o=p[1],"string"==typeof p[2]?s=p[2]:a=p[2],e.local("pLocals"),r){if(ri(e,c,o?42:43),a)e.appendU8(a);else {if(!s)throw new Error("internal error");e.callImport(s);}return oi(e,i,o?56:57),true}if(ri(e,c,o?42:43),ri(e,l,o?42:43),a)e.appendU8(a);else {if(!s)throw new Error("internal error");e.callImport(s);}return oi(e,i,o?56:57),true}function ki(e,t,n){const r=n>=87&&n<=112,o=n>=107&&n<=112,s=n>=95&&n<=106||n>=120&&n<=127||o,a=n>=101&&n<=106||n>=124&&n<=127||o;let i,c,l=-1,p=0,u=1;o?(i=Ca(t,1),c=Ca(t,2),l=Ca(t,3),p=Da(t,4),u=Da(t,5)):s?a?r?(i=Ca(t,1),c=Ca(t,2),p=Da(t,3)):(i=Ca(t,2),c=Ca(t,1),p=Da(t,3)):r?(i=Ca(t,1),c=Ca(t,2),l=Ca(t,3)):(i=Ca(t,3),c=Ca(t,1),l=Ca(t,2)):r?(c=Ca(t,2),i=Ca(t,1)):(c=Ca(t,1),i=Ca(t,2));let d,f=54;switch(n){case 87:case 95:case 101:case 107:d=44;break;case 88:case 96:case 102:case 108:d=45;break;case 89:case 97:case 103:case 109:d=46;break;case 90:case 98:case 104:case 110:d=47;break;case 113:case 120:case 124:d=40,f=58;break;case 114:case 121:case 125:d=40,f=59;break;case 91:case 99:case 105:case 111:case 115:case 122:case 126:case 119:d=40;break;case 93:case 117:d=42,f=56;break;case 94:case 118:d=43,f=57;break;case 92:case 100:case 106:case 112:case 116:case 123:case 127:d=41,f=55;break;default:return  false}const _=ni(e,c,40,true,true);return _||li(e,c,t,false),r?(e.local("pLocals"),_?mt(ni(e,c,40,false,true),"Unknown jiterpreter cprop failure"):e.local("cknull_ptr"),o?(ri(e,l,40),0!==p&&(e.i32_const(p),e.appendU8(106),p=0),1!==u&&(e.i32_const(u),e.appendU8(108)),e.appendU8(106)):s&&l>=0?(ri(e,l,40),e.appendU8(106)):p<0&&(e.i32_const(p),e.appendU8(106),p=0),e.appendU8(d),e.appendMemarg(p,0),oi(e,i,f)):119===n?(_?mt(ni(e,c,40,false,true),"Unknown jiterpreter cprop failure"):e.local("cknull_ptr"),si(e,i,0),e.callImport("copy_ptr")):(_?mt(ni(e,c,40,false,true),"Unknown jiterpreter cprop failure"):e.local("cknull_ptr"),s&&l>=0?(ri(e,l,40),e.appendU8(106)):p<0&&(e.i32_const(p),e.appendU8(106),p=0),ri(e,i,d),e.appendU8(f),e.appendMemarg(p,0)),true}function Si(e,t,n,r,o){e.block(),ri(e,r,40),e.local("index",34);let s="cknull_ptr";e.options.zeroPageOptimization&&aa()?(da(8,1),ri(e,n,40),s="src_ptr",e.local(s,34)):li(e,n,t,true),e.appendU8(40),e.appendMemarg(ea(9),2),e.appendU8(73),e.appendU8(13),e.appendULeb(0),Ws(e,t,9),e.endBlock(),e.local(s),e.i32_const(ea(1)),e.appendU8(106),e.local("index"),1!=o&&(e.i32_const(o),e.appendU8(108)),e.appendU8(106);}function vi(e,t,n,r){const o=r<=328&&r>=315||341===r,s=Ca(n,o?2:1),a=Ca(n,o?1:3),i=Ca(n,o?3:2);let c,l,p=54;switch(r){case 341:return e.local("pLocals"),li(e,s,n,true),e.appendU8(40),e.appendMemarg(ea(9),2),oi(e,a,54),true;case 326:return e.local("pLocals"),l=Ca(n,4),Si(e,n,s,i,l),oi(e,a,54),true;case 337:return e.block(),ri(e,Ca(n,1),40),ri(e,Ca(n,2),40),ri(e,Ca(n,3),40),e.callImport("stelemr_tc"),e.appendU8(13),e.appendULeb(0),Ws(e,n,10),e.endBlock(),true;case 340:return Si(e,n,s,i,4),si(e,a,0),e.callImport("copy_ptr"),true;case 324:case 320:case 319:case 333:l=4,c=40;break;case 315:l=1,c=44;break;case 316:l=1,c=45;break;case 330:case 329:l=1,c=40,p=58;break;case 317:l=2,c=46;break;case 318:l=2,c=47;break;case 332:case 331:l=2,c=40,p=59;break;case 322:case 335:l=4,c=42,p=56;break;case 321:case 334:l=8,c=41,p=55;break;case 323:case 336:l=8,c=43,p=57;break;case 325:{const t=Ca(n,4);return e.local("pLocals"),e.i32_const(Ca(n,1)),e.appendU8(106),Si(e,n,s,i,t),Ys(e,t),Ka(Ca(n,1),t),true}case 338:{const r=Ca(n,5),o=za(t,Ca(n,4));return Si(e,n,s,i,r),si(e,a,0),e.ptr_const(o),e.callImport("value_copy"),true}case 339:{const t=Ca(n,5);return Si(e,n,s,i,t),si(e,a,0),Ys(e,t),true}default:return  false}return o?(e.local("pLocals"),Si(e,n,s,i,l),e.appendU8(c),e.appendMemarg(0,0),oi(e,a,p)):(Si(e,n,s,i,l),ri(e,a,c),e.appendU8(p),e.appendMemarg(0,0)),true}function Ui(e,t,n){const r=`${t}_${n.toString(16)}`;return "object"!=typeof e.importedFunctions[r]&&e.defineImportedFunction("s",r,t,false,n),r}function Ti(e,t,n,r,s,a){if(e.options.enableSimd&&ct$1.featureWasmSimd)switch(s){case 2:if(function(e,t,n){const r=o$1.mono_jiterp_get_simd_opcode(1,n),s=Na[n];if(s)return xi(e,t),e.appendSimd(s),oi(e,Ca(t,1),54),true;if(r>=0)return Ra.has(n)?(e.local("pLocals"),ri(e,Ca(t,2),40),e.appendSimd(r,true),e.appendMemarg(0,0),Ei(e,t)):(xi(e,t),e.appendSimd(r),Ei(e,t)),true;switch(n){case 6:case 7:case 8:case 9:{const r=Oa[n];return e.local("pLocals"),e.v128_const(0),ri(e,Ca(t,2),r[0]),e.appendSimd(r[1]),e.appendU8(0),oi(e,Ca(t,1),253,11),true}case 14:return xi(e,t,7),Ei(e,t),true;case 15:return xi(e,t,8),Ei(e,t),true;case 16:return xi(e,t,9),Ei(e,t),true;case 17:return xi(e,t,10),Ei(e,t),true;default:return  false}}(e,t,a))return  true;break;case 3:if(function(e,t,n){const r=o$1.mono_jiterp_get_simd_opcode(2,n);if(r>=0){const o=ja.has(n),s=$a[n];if(o)e.local("pLocals"),ri(e,Ca(t,2),253,0),ri(e,Ca(t,3),40),e.appendSimd(r),Ei(e,t);else if(Array.isArray(s)){const n=Ga(e,Ca(t,3)),o=s[0];if("number"!=typeof n)return He$1(`${e.functions[0].name}: Non-constant lane index passed to ExtractScalar`),false;if(n>=o||n<0)return He$1(`${e.functions[0].name}: ExtractScalar index ${n} out of range (0 - ${o-1})`),false;e.local("pLocals"),ri(e,Ca(t,2),253,0),e.appendSimd(r),e.appendU8(n),oi(e,Ca(t,1),s[1]);}else Ii(e,t),e.appendSimd(r),Ei(e,t);return  true}switch(n){case 191:return ri(e,Ca(t,2),40),ri(e,Ca(t,3),253,0),e.appendSimd(11),e.appendMemarg(0,0),true;case 10:case 11:return Ii(e,t),e.appendSimd(214),e.appendSimd(195),11===n&&e.appendU8(69),oi(e,Ca(t,1),54),true;case 12:case 13:{const r=13===n,o=r?71:65;return e.local("pLocals"),ri(e,Ca(t,2),253,0),e.local("math_lhs128",34),ri(e,Ca(t,3),253,0),e.local("math_rhs128",34),e.appendSimd(o),e.local("math_lhs128"),e.local("math_lhs128"),e.appendSimd(o),e.local("math_rhs128"),e.local("math_rhs128"),e.appendSimd(o),e.appendSimd(80),e.appendSimd(77),e.appendSimd(80),e.appendSimd(r?195:163),oi(e,Ca(t,1),54),true}case 47:{const n=Ca(t,3),r=Ga(e,n);return e.local("pLocals"),ri(e,Ca(t,2),253,0),"object"==typeof r?(e.appendSimd(12),e.appendBytes(r)):ri(e,n,253,0),e.appendSimd(14),Ei(e,t),true}case 48:case 49:return function(e,t,n){const r=16/n,o=Ca(t,3),s=Ga(e,o);if(2!==r&&4!==r&&mt(false,"Unsupported shuffle element size"),e.local("pLocals"),ri(e,Ca(t,2),253,0),"object"==typeof s){const t=new Uint8Array(bc),o=2===r?new Uint16Array(s.buffer,s.byteOffset,n):new Uint32Array(s.buffer,s.byteOffset,n);for(let e=0,s=0;e<n;e++,s+=r){const n=o[e];for(let e=0;e<r;e++)t[s+e]=n*r+e;}e.appendSimd(12),e.appendBytes(t);}else {ri(e,o,253,0),4===n&&(e.v128_const(0),e.appendSimd(134)),e.v128_const(0),e.appendSimd(102),e.appendSimd(12);for(let t=0;t<n;t++)for(let n=0;n<r;n++)e.appendU8(t);ct$1.featureWasmRelaxedSimd?e.appendSimd(256):e.appendSimd(14),e.i32_const(4===n?2:1),e.appendSimd(107),e.appendSimd(12);for(let t=0;t<n;t++)for(let t=0;t<r;t++)e.appendU8(t);e.appendSimd(80);}return e.appendSimd(14),Ei(e,t),true}(e,t,48===n?8:4);default:return  false}return  false}(e,t,a))return  true;break;case 4:if(function(e,t,n){const r=o$1.mono_jiterp_get_simd_opcode(3,n);if(r>=0){const o=La[n],s=Ba[n];if(Array.isArray(o)){const n=o[0],s=Ga(e,Ca(t,3));if("number"!=typeof s)return He$1(`${e.functions[0].name}: Non-constant lane index passed to ReplaceScalar`),false;if(s>=n||s<0)return He$1(`${e.functions[0].name}: ReplaceScalar index ${s} out of range (0 - ${n-1})`),false;e.local("pLocals"),ri(e,Ca(t,2),253,0),ri(e,Ca(t,4),o[1]),e.appendSimd(r),e.appendU8(s),Ei(e,t);}else if(Array.isArray(s)){const n=s[0],o=Ga(e,Ca(t,4));if("number"!=typeof o)return He$1(`${e.functions[0].name}: Non-constant lane index passed to store method`),false;if(o>=n||o<0)return He$1(`${e.functions[0].name}: Store lane ${o} out of range (0 - ${n-1})`),false;ri(e,Ca(t,2),40),ri(e,Ca(t,3),253,0),e.appendSimd(r),e.appendMemarg(0,0),e.appendU8(o);}else !function(e,t){e.local("pLocals"),ri(e,Ca(t,2),253,0),ri(e,Ca(t,3),253,0),ri(e,Ca(t,4),253,0);}(e,t),e.appendSimd(r),Ei(e,t);return  true}switch(n){case 0:return e.local("pLocals"),ri(e,Ca(t,3),253,0),ri(e,Ca(t,4),253,0),ri(e,Ca(t,2),253,0),e.appendSimd(82),Ei(e,t),true;case 7:{const n=Ga(e,Ca(t,4));if("object"!=typeof n)return He$1(`${e.functions[0].name}: Non-constant indices passed to PackedSimd.Shuffle`),false;for(let t=0;t<32;t++){const r=n[t];if(r<0||r>31)return He$1(`${e.functions[0].name}: Shuffle lane index #${t} (${r}) out of range (0 - 31)`),false}return e.local("pLocals"),ri(e,Ca(t,2),253,0),ri(e,Ca(t,3),253,0),e.appendSimd(13),e.appendBytes(n),Ei(e,t),true}default:return  false}}(e,t,a))return  true}switch(n){case 652:if(e.options.enableSimd&&ct$1.featureWasmSimd){e.local("pLocals");const n=K$1().slice(t+4,t+4+bc);e.v128_const(n),Ei(e,t),Ha.set(Ca(t,1),{type:"v128",value:n});}else si(e,Ca(t,1),bc),e.ptr_const(t+4),Ys(e,bc);return  true;case 653:case 654:case 655:case 656:{const r=xa[n],o=bc/r,s=Ca(t,1),a=Ca(t,2),i=Ia[n],c=Aa[n];for(let t=0;t<o;t++)e.local("pLocals"),ri(e,a+t*yc,i),oi(e,s+t*r,c);return  true}case 657:{Ms[r]=(Ms[r]||0)+1,si(e,Ca(t,1),bc),si(e,Ca(t,2),0);const n=Ui(e,"simd_p_p",o$1.mono_jiterp_get_simd_intrinsic(1,a));return e.callImport(n),true}case 658:{Ms[r]=(Ms[r]||0)+1,si(e,Ca(t,1),bc),si(e,Ca(t,2),0),si(e,Ca(t,3),0);const n=Ui(e,"simd_p_pp",o$1.mono_jiterp_get_simd_intrinsic(2,a));return e.callImport(n),true}case 659:{Ms[r]=(Ms[r]||0)+1,si(e,Ca(t,1),bc),si(e,Ca(t,2),0),si(e,Ca(t,3),0),si(e,Ca(t,4),0);const n=Ui(e,"simd_p_ppp",o$1.mono_jiterp_get_simd_intrinsic(3,a));return e.callImport(n),true}default:return Ve$1(`jiterpreter emit_simd failed for ${r}`),false}}function Ei(e,t){oi(e,Ca(t,1),253,11);}function xi(e,t,n){e.local("pLocals"),ri(e,Ca(t,2),253,n||0);}function Ii(e,t){e.local("pLocals"),ri(e,Ca(t,2),253,0),ri(e,Ca(t,3),253,0);}function Ai(e,t,n){if(559===n)return  true;if(!e.options.enableAtomics)return  false;const r=Ta[n];if(r){const n=r[2]>2;return e.local("pLocals"),li(e,Ca(t,2),t,true),ri(e,Ca(t,3),n?41:40),e.appendAtomic(r[0],false),e.appendMemarg(0,r[2]),0!==r[1]&&e.appendU8(r[1]),oi(e,Ca(t,1),n?55:54),true}const o=Ea[n];if(o){const n=o[2]>2;return e.local("pLocals"),li(e,Ca(t,2),t,true),ri(e,Ca(t,4),n?41:40),ri(e,Ca(t,3),n?41:40),e.appendAtomic(o[0],false),e.appendMemarg(0,o[2]),0!==o[1]&&e.appendU8(o[1]),oi(e,Ca(t,1),n?55:54),true}return  false}function ji(e,t,n){const r=Ja(t,271),o=function(e){271!==O$1(e)&&mt(false,"decodeSwitch called on a non-switch");const t=Fa(e,2),n=[];for(let r=0;r<t;r++){const t=e+8+4*r,o=t+2*P$1(t);n.push(o);}return n}(t);let s=false;if(o.length>e.options.maxSwitchSize)s=true;else for(const n of o)n>t&&e.branchTargets.add(n);if(s)return da(14,o.length),Ws(e,t,27),true;const a=t+2*r;return e.branchTargets.add(a),e.block(),ri(e,Ca(t,1),40),e.cfg.jumpTable(o,a),e.endBlock(),Hs(e,t,n,28),true}const $i=64;let Li,Ri,Bi,Ni=0;const Oi={};function Ci(){return Ri||(Ri=[oa("interp_entry_prologue",ta("mono_jiterp_interp_entry_prologue")),oa("interp_entry",ta("mono_jiterp_interp_entry")),oa("unbox",ta("mono_jiterp_object_unbox")),oa("stackval_from_data",ta("mono_jiterp_stackval_from_data"))],Ri)}let Di,Fi=class{constructor(e,t,n,r,o,s,a,i){this.imethod=e,this.method=t,this.argumentCount=n,this.unbox=o,this.hasThisReference=s,this.hasReturnValue=a,this.paramTypes=new Array(n);for(let e=0;e<n;e++)this.paramTypes[e]=P$1(r+4*e);this.defaultImplementation=i,this.result=0,this.hitCount=0;}generateName(){const e=o$1.mono_wasm_method_get_full_name(this.method);try{const t=Le$1(e);this.name=t;let n=t;if(n){const e=24;n.length>e&&(n=n.substring(n.length-e,n.length)),n=`${this.imethod.toString(16)}_${n}`;}else n=`${this.imethod.toString(16)}_${this.hasThisReference?"i":"s"}${this.hasReturnValue?"_r":""}_${this.argumentCount}`;this.traceName=n;}finally{e&&m$1(e);}}getTraceName(){return this.traceName||this.generateName(),this.traceName||"unknown"}getName(){return this.name||this.generateName(),this.name||"unknown"}};function Pi(){const e=[];let t=0;for(;0!=(t=o$1.mono_jiterp_tlqueue_next(1));){const n=Oi[t];n?e.push(n):Ve$1(`Failed to find corresponding info for method ptr ${t} from jit queue!`);}if(!e.length)return;const n=4*e.length+1;let r=Li;if(r?r.clear(n):(Li=r=new Cs(n),r.defineType("unbox",{pMonoObject:127},127,true),r.defineType("interp_entry_prologue",{pData:127,this_arg:127},127,true),r.defineType("interp_entry",{pData:127,res:127},64,true),r.defineType("stackval_from_data",{type:127,result:127,value:127},64,true)),r.options.wasmBytesLimit<=ua(6))return;const s=zs();let a=0,i=true,c=false;try{r.appendU32(1836278016),r.appendU32(1);for(let t=0;t<e.length;t++){const n=e[t],o={};n.hasThisReference&&(o.this_arg=127),n.hasReturnValue&&(o.res=127);for(let e=0;e<n.argumentCount;e++)o[`arg${e}`]=127;o.rmethod=127,r.defineType(n.getTraceName(),o,64,!1);}r.generateTypeSection();const t=Ci();r.compressImportNames=!0;for(let e=0;e<t.length;e++)t[e]||mt(!1,`trace #${e} missing`),r.defineImportedFunction("i",t[e][0],t[e][1],!0,t[e][2]);for(let e=0;e<t.length;e++)r.markImportAsUsed(t[e][0]);r._generateImportSection(!1),r.beginSection(3),r.appendULeb(e.length);for(let t=0;t<e.length;t++){const n=e[t].getTraceName();r.functionTypes[n]||mt(!1,"func type missing"),r.appendULeb(r.functionTypes[n][0]);}r.beginSection(7),r.appendULeb(e.length);for(let t=0;t<e.length;t++){const n=e[t].getTraceName();r.appendName(n),r.appendU8(0),r.appendULeb(r.importedFunctionCount+t);}r.beginSection(10),r.appendULeb(e.length);for(let t=0;t<e.length;t++){const n=e[t],o=n.getTraceName();r.beginFunction(o,{sp_args:127,need_unbox:127,scratchBuffer:127}),zi(r,n),r.appendU8(11),r.endFunction(!0);}r.endSection(),a=zs();const n=r.getArrayView();da(6,n.length);const o=new WebAssembly.Module(n),s=r.getWasmImports(),c=new WebAssembly.Instance(o,s);for(let t=0;t<e.length;t++){const n=e[t],r=n.getTraceName(),o=c.exports[r];Bi.set(n.result,o),i=!1;}da(2,e.length);}catch(e){c=true,i=false,He$1(`interp_entry code generation failed: ${e}`),Zs();}finally{const t=zs();if(a?(da(11,a-s),da(12,t-a)):da(11,t-s),c){Ve$1(`// ${e.length} trampolines generated, blob follows //`);let t="",n=0;try{r.inSection&&r.endSection();}catch(e){}const o=r.getArrayView(false,true);for(let e=0;e<o.length;e++){const r=o[e];r<16&&(t+="0"),t+=r.toString(16),t+=" ",t.length%10==0&&(Ve$1(`${n}\t${t}`),t="",n=e+1);}Ve$1(`${n}\t${t}`),Ve$1("// end blob //");}else i&&!c&&He$1("failed to generate trampoline for unknown reason");}}function Mi(e,t,n,r,s){const a=o$1.mono_jiterp_type_get_raw_value_size(n),i=o$1.mono_jiterp_get_arg_offset(t,0,s);switch(a){case 256:e.local("sp_args"),e.local(r),e.appendU8(54),e.appendMemarg(i,2);break;case  -1:case  -2:case 1:case 2:case 4:switch(e.local("sp_args"),e.local(r),a){case  -1:e.appendU8(45),e.appendMemarg(0,0);break;case 1:e.appendU8(44),e.appendMemarg(0,0);break;case  -2:e.appendU8(47),e.appendMemarg(0,0);break;case 2:e.appendU8(46),e.appendMemarg(0,0);break;case 4:e.appendU8(40),e.appendMemarg(0,2);}e.appendU8(54),e.appendMemarg(i,2);break;default:e.ptr_const(n),e.local("sp_args"),e.i32_const(i),e.appendU8(106),e.local(r),e.callImport("stackval_from_data");}}function zi(e,t){const n=_$1($i);g$1(n,$i),T$1(n+ea(13),t.paramTypes.length+(t.hasThisReference?1:0)),t.hasThisReference&&(e.block(),e.local("rmethod"),e.i32_const(1),e.appendU8(113),e.appendU8(69),e.appendU8(13),e.appendULeb(0),e.local("this_arg"),e.callImport("unbox"),e.local("this_arg",33),e.endBlock()),e.ptr_const(n),e.local("scratchBuffer",34),e.local("rmethod"),e.i32_const(-2),e.appendU8(113),e.appendU8(54),e.appendMemarg(ea(6),0),e.local("scratchBuffer"),t.hasThisReference?e.local("this_arg"):e.i32_const(0),e.callImport("interp_entry_prologue"),e.local("sp_args",33),t.hasThisReference&&Mi(e,t.imethod,0,"this_arg",0);for(let n=0;n<t.paramTypes.length;n++){const r=t.paramTypes[n];Mi(e,t.imethod,r,`arg${n}`,n+(t.hasThisReference?1:0));}return e.local("scratchBuffer"),t.hasReturnValue?e.local("res"):e.i32_const(0),e.callImport("interp_entry"),e.appendU8(15),true}const Vi=16,Wi=0;let Hi,qi,Gi=0;const Ji=[],Xi={},Qi={};class Yi{constructor(e,t,n,r,s){this.queue=[],r||mt(false,"Expected nonzero arg_offsets pointer"),this.method=e,this.rmethod=t,this.catchExceptions=s,this.cinfo=n,this.addr=P$1(n+0),this.wrapper=P$1(n+8),this.signature=P$1(n+12),this.noWrapper=0!==N$1(n+28),this.hasReturnValue=-1!==F$1(n+24),this.returnType=o$1.mono_jiterp_get_signature_return_type(this.signature),this.paramCount=o$1.mono_jiterp_get_signature_param_count(this.signature),this.hasThisReference=0!==o$1.mono_jiterp_get_signature_has_this(this.signature);const a=o$1.mono_jiterp_get_signature_params(this.signature);this.paramTypes=new Array(this.paramCount);for(let e=0;e<this.paramCount;e++)this.paramTypes[e]=P$1(a+4*e);const i=this.paramCount+(this.hasThisReference?1:0);this.argOffsets=new Array(this.paramCount);for(let e=0;e<i;e++)this.argOffsets[e]=P$1(r+4*e);this.target=this.noWrapper?this.addr:this.wrapper,this.result=0,this.wasmNativeReturnType=this.returnType&&this.hasReturnValue?ec[o$1.mono_jiterp_type_to_stind(this.returnType)]:64,this.wasmNativeSignature=this.paramTypes.map((e=>ec[o$1.mono_jiterp_type_to_ldind(e)])),this.enableDirect=fa().directJitCalls&&!this.noWrapper&&this.wasmNativeReturnType&&(0===this.wasmNativeSignature.length||this.wasmNativeSignature.every((e=>e))),this.enableDirect&&(this.target=this.addr);let c=this.target.toString(16);const l=Gi++;this.name=`${this.enableDirect?"jcp":"jcw"}_${c}_${l.toString(16)}`;}}function Zi(e){let t=Ji[e];return t||(e>=Ji.length&&(Ji.length=e+1),qi||(qi=qs()),Ji[e]=t=qi.get(e)),t}function Ki(){const e=[];let t=0;for(;0!=(t=o$1.mono_jiterp_tlqueue_next(0));){const n=Qi[t];if(n)for(let t=0;t<n.length;t++)0===n[t].result&&e.push(n[t]);else Ve$1(`Failed to find corresponding info list for method ptr ${t} from jit queue!`);}if(!e.length)return;let n=Hi;if(n?n.clear(0):(Hi=n=new Cs(0),n.defineType("trampoline",{ret_sp:127,sp:127,ftndesc:127,thrown:127},64,true),n.defineType("begin_catch",{ptr:127},64,true),n.defineType("end_catch",{},64,true),n.defineImportedFunction("i","begin_catch","begin_catch",true,ta("mono_jiterp_begin_catch")),n.defineImportedFunction("i","end_catch","end_catch",true,ta("mono_jiterp_end_catch"))),n.options.wasmBytesLimit<=ua(6))return void o$1.mono_jiterp_tlqueue_clear(0);n.options.enableWasmEh&&(ct$1.featureWasmEh||(pa({enableWasmEh:false}),n.options.enableWasmEh=false));const r=zs();let s=0,a=true,i=false;const c=[];try{qi||(qi=qs()),n.appendU32(1836278016),n.appendU32(1);for(let t=0;t<e.length;t++){const r=e[t],o={};if(r.enableDirect){r.hasThisReference&&(o.this=127);for(let e=0;e<r.wasmNativeSignature.length;e++)o[`arg${e}`]=r.wasmNativeSignature[e];o.rgctx=127;}else {const e=(r.hasThisReference?1:0)+(r.hasReturnValue?1:0)+r.paramCount;for(let t=0;t<e;t++)o[`arg${t}`]=127;o.ftndesc=127;}n.defineType(r.name,o,r.enableDirect?r.wasmNativeReturnType:64,!1);const s=Zi(r.target);"function"!=typeof s&&mt(!1,`expected call target to be function but was ${s}`),c.push([r.name,r.name,s]);}n.generateTypeSection(),n.compressImportNames=!0;for(let e=0;e<c.length;e++)n.defineImportedFunction("i",c[e][0],c[e][1],!1,c[e][2]);for(let e=0;e<c.length;e++)n.markImportAsUsed(c[e][0]);n.markImportAsUsed("begin_catch"),n.markImportAsUsed("end_catch"),n._generateImportSection(!1),n.beginSection(3),n.appendULeb(e.length),n.functionTypes.trampoline||mt(!1,"func type missing");for(let t=0;t<e.length;t++)n.appendULeb(n.functionTypes.trampoline[0]);n.beginSection(7),n.appendULeb(e.length);for(let t=0;t<e.length;t++){const r=e[t];n.appendName(r.name),n.appendU8(0),n.appendULeb(n.importedFunctionCount+t);}n.beginSection(10),n.appendULeb(e.length);for(let t=0;t<e.length;t++){const r=e[t];if(n.beginFunction("trampoline",{old_sp:127}),!oc(n,r))throw new Error(`Failed to generate ${r.name}`);n.appendU8(11),n.endFunction(!0);}n.endSection(),s=zs();const t=n.getArrayView();da(6,t.length);const r=new WebAssembly.Module(t),i=n.getWasmImports(),l=new WebAssembly.Instance(r,i);for(let t=0;t<e.length;t++){const n=e[t],r=Gs(1,l.exports[n.name]);if(n.result=r,r>0){o$1.mono_jiterp_register_jit_call_thunk(n.cinfo,r);for(let e=0;e<n.queue.length;e++)o$1.mono_jiterp_register_jit_call_thunk(n.queue[e],r);n.enableDirect&&da(4,1),da(3,1);}n.queue.length=0,a=!1;}}catch(e){i=true,a=false,He$1(`jit_call code generation failed: ${e}`),Zs();}finally{const t=zs();if(s?(da(11,s-r),da(12,t-s)):da(11,t-r),i||a)for(let t=0;t<e.length;t++)e[t].result=-1;if(i){Ve$1(`// ${e.length} jit call wrappers generated, blob follows //`);for(let t=0;t<e.length;t++)Ve$1(`// #${t} === ${e[t].name} hasThis=${e[t].hasThisReference} hasRet=${e[t].hasReturnValue} wasmArgTypes=${e[t].wasmNativeSignature}`);let t="",r=0;try{n.inSection&&n.endSection();}catch(e){}const o=n.getArrayView(false,true);for(let e=0;e<o.length;e++){const n=o[e];n<16&&(t+="0"),t+=n.toString(16),t+=" ",t.length%10==0&&(Ve$1(`${r}\t${t}`),t="",r=e+1);}Ve$1(`${r}\t${t}`),Ve$1("// end blob //");}else a&&!i&&He$1("failed to generate trampoline for unknown reason");}}const ec={65535:127,70:127,71:127,72:127,73:127,74:127,75:127,76:126,77:127,78:125,79:124,80:127,81:127,82:127,83:127,84:127,85:126,86:125,87:124,223:127},tc={70:44,71:45,72:46,73:47,74:40,75:40,76:41,77:40,78:42,79:43,80:40,81:54,82:58,83:59,84:54,85:55,86:56,87:57,223:54};function nc(e,t,n){e.local("sp"),e.appendU8(n),e.appendMemarg(t,0);}function rc(e,t){e.local("sp"),e.i32_const(t),e.appendU8(106);}function oc(e,t){let n=0;e.options.enableWasmEh&&e.block(64,6),t.hasReturnValue&&t.enableDirect&&e.local("ret_sp"),t.hasThisReference&&(nc(e,t.argOffsets[0],40),n++),t.hasReturnValue&&!t.enableDirect&&e.local("ret_sp");for(let r=0;r<t.paramCount;r++){const s=t.argOffsets[n+r];if(N$1(P$1(t.cinfo+Vi)+r)==Wi)nc(e,s,40);else if(t.enableDirect){const n=o$1.mono_jiterp_type_to_ldind(t.paramTypes[r]);if(n||mt(false,`No load opcode for ${t.paramTypes[r]}`),65535===n)rc(e,s);else {const o=tc[n];if(!o)return He$1(`No wasm load op for arg #${r} type ${t.paramTypes[r]} cil opcode ${n}`),false;nc(e,s,o);}}else rc(e,s);}if(e.local("ftndesc"),(t.enableDirect||t.noWrapper)&&(e.appendU8(40),e.appendMemarg(4,0)),e.callImport(t.name),t.hasReturnValue&&t.enableDirect){const n=o$1.mono_jiterp_type_to_stind(t.returnType),r=tc[n];if(!r)return He$1(`No wasm store op for return type ${t.returnType} cil opcode ${n}`),false;e.appendU8(r),e.appendMemarg(0,0);}return e.options.enableWasmEh&&(e.appendU8(7),e.appendULeb(e.getTypeIndex("__cpp_exception")),e.callImport("begin_catch"),e.callImport("end_catch"),e.local("thrown"),e.i32_const(1),e.appendU8(54),e.appendMemarg(0,2),e.endBlock()),e.appendU8(15),true}const sc=30;let ac,ic,cc=false;const lc=[],pc=[];class uc{constructor(e){this.name=e,this.eip=0;}}class dc{constructor(e,t,n){this.ip=e,this.index=t,this.isVerbose=!!n;}get hitCount(){return o$1.mono_jiterp_get_trace_hit_count(this.index)}}const fc={};let _c=1;const mc={},hc={},gc=4,bc=16,yc=8;let wc,kc;const Sc=["asin","acos","atan","asinh","acosh","atanh","cos","sin","tan","cosh","sinh","tanh","exp","log","log2","log10","cbrt"],vc=["fmod","atan2","pow"],Uc=["asinf","acosf","atanf","asinhf","acoshf","atanhf","cosf","sinf","tanf","coshf","sinhf","tanhf","expf","logf","log2f","log10f","cbrtf"],Tc=["fmodf","atan2f","powf"];function Ec(e,t,n){if(o$1.mono_jiterp_trace_bailout(n),14===n)return e;const r=hc[t];if(!r)return void He$1(`trace info not found for ${t}`);let s=r.bailoutCounts;s||(r.bailoutCounts=s={});const a=s[n];return s[n]=a?a+1:1,r.bailoutCount?r.bailoutCount++:r.bailoutCount=1,e}function xc(){if(kc)return kc;kc=[oa("bailout",Ec),oa("copy_ptr",ta("mono_wasm_copy_managed_pointer")),oa("entry",ta("mono_jiterp_increase_entry_count")),oa("value_copy",ta("mono_jiterp_value_copy")),oa("gettype",ta("mono_jiterp_gettype_ref")),oa("castv2",ta("mono_jiterp_cast_v2")),oa("hasparent",ta("mono_jiterp_has_parent_fast")),oa("imp_iface",ta("mono_jiterp_implements_interface")),oa("imp_iface_s",ta("mono_jiterp_implements_special_interface")),oa("box",ta("mono_jiterp_box_ref")),oa("localloc",ta("mono_jiterp_localloc")),["ckovr_i4","overflow_check_i4",ta("mono_jiterp_overflow_check_i4")],["ckovr_u4","overflow_check_i4",ta("mono_jiterp_overflow_check_u4")],oa("newobj_i",ta("mono_jiterp_try_newobj_inlined")),oa("newstr",ta("mono_jiterp_try_newstr")),oa("newarr",ta("mono_jiterp_try_newarr")),oa("ld_del_ptr",ta("mono_jiterp_ld_delegate_method_ptr")),oa("ldtsflda",ta("mono_jiterp_ldtsflda")),oa("conv",ta("mono_jiterp_conv")),oa("relop_fp",ta("mono_jiterp_relop_fp")),oa("safepoint",ta("mono_jiterp_do_safepoint")),oa("hashcode",ta("mono_jiterp_get_hashcode")),oa("try_hash",ta("mono_jiterp_try_get_hashcode")),oa("hascsize",ta("mono_jiterp_object_has_component_size")),oa("hasflag",ta("mono_jiterp_enum_hasflag")),oa("array_rank",ta("mono_jiterp_get_array_rank")),["a_elesize","array_rank",ta("mono_jiterp_get_array_element_size")],oa("stfld_o",ta("mono_jiterp_set_object_field")),["stelemr_tc","stelemr",ta("mono_jiterp_stelem_ref")],oa("fma",ta("fma")),oa("fmaf",ta("fmaf"))],pc.length>0&&(kc.push(["trace_eip","trace_eip",Ic]),kc.push(["trace_args","trace_eip",Ac])),(ct$1.emscriptenBuildOptions.enableEventPipe||ct$1.emscriptenBuildOptions.enableDevToolsProfiler)&&(kc.push(oa("prof_enter",ta("mono_jiterp_prof_enter"))),kc.push(oa("prof_samplepoint",ta("mono_jiterp_prof_samplepoint"))),kc.push(oa("prof_leave",ta("mono_jiterp_prof_leave"))));const e=(e,t)=>{for(let n=0;n<e.length;n++){const r=e[n];kc.push([r,t,ta(r)]);}};return e(Uc,"mathop_f_f"),e(Tc,"mathop_ff_f"),e(Sc,"mathop_d_d"),e(vc,"mathop_dd_d"),kc}function Ic(e,t){const n=fc[e];if(!n)throw new Error(`Unrecognized instrumented trace id ${e}`);n.eip=t,ac=n;}function Ac(e,t){if(!ac)throw new Error("No trace active");ac.operand1=e>>>0,ac.operand2=t>>>0;}function jc(e,t,n,r){if("number"==typeof r)o$1.mono_jiterp_adjust_abort_count(r,1),r=$s(r);else {let e=mc[r];"number"!=typeof e?e=1:e++,mc[r]=e;}hc[e].abortReason=r;}function $c(e){if(!ct$1.runtimeReady)return;if(ic||(ic=fa()),!ic.enableStats)return;const t=ua(9),n=ua(10),r=ua(7),s=ua(8),a=ua(3),i=ua(4),c=ua(2),l=ua(1),p=ua(0),u=ua(6),d=ua(11),f=ua(12),_=ua(13),m=ua(14),h=t/(t+n)*100,g=o$1.mono_jiterp_get_rejected_trace_count(),b=ic.eliminateNullChecks?r.toString():"off",y=ic.zeroPageOptimization?s.toString()+(aa()?"":" (disabled)"):"off",w=ic.enableBackwardBranches?`emitted: ${t}, failed: ${n} (${h.toFixed(1)}%)`:": off",k=a?ic.directJitCalls?`direct jit calls: ${i} (${(i/a*100).toFixed(1)}%)`:"direct jit calls: off":"";if(Ve$1(`// jitted ${u}b; ${l} traces (${(l/p*100).toFixed(1)}%) (${g} rejected); ${a} jit_calls; ${c} interp_entries`),Ve$1(`// cknulls pruned: ${b}, fused: ${y}; back-brs ${w}; switch tgts ${_}/${m+_}; ${k}`),Ve$1(`// time: ${0|d}ms generating, ${0|f}ms compiling wasm.`),!e){if(ic.countBailouts){const e=Object.values(hc);e.sort(((e,t)=>(t.bailoutCount||0)-(e.bailoutCount||0)));for(let e=0;e<ha.length;e++){const t=o$1.mono_jiterp_get_trace_bailout_count(e);t&&Ve$1(`// traces bailed out ${t} time(s) due to ${ha[e]}`);}for(let t=0,n=0;t<e.length&&n<sc;t++){const r=e[t];if(r.bailoutCount){n++,Ve$1(`${r.name}: ${r.bailoutCount} bailout(s)`);for(const e in r.bailoutCounts)Ve$1(`  ${ha[e]} x${r.bailoutCounts[e]}`);}}}if(ic.estimateHeat){const e={},t=Object.values(hc);for(let n=0;n<t.length;n++){const r=t[n];r.abortReason&&"end-of-body"!==r.abortReason&&(e[r.abortReason]?e[r.abortReason]+=r.hitCount:e[r.abortReason]=r.hitCount);}t.sort(((e,t)=>t.hitCount-e.hitCount)),Ve$1("// hottest failed traces:");for(let e=0,n=0;e<t.length&&n<sc;e++)if(t[e].name&&!(t[e].fnPtr||t[e].name.indexOf("Xunit.")>=0)){if(t[e].abortReason){if(t[e].abortReason.startsWith("mono_icall_")||t[e].abortReason.startsWith("ret."))continue;switch(t[e].abortReason){case "trace-too-small":case "trace-too-big":case "call":case "callvirt.fast":case "calli.nat.fast":case "calli.nat":case "call.delegate":case "newobj":case "newobj_vt":case "newobj_slow":case "switch":case "rethrow":case "end-of-body":case "ret":case "intrins_marvin_block":case "intrins_ascii_chars_to_uppercase":continue}}n++,Ve$1(`${t[e].name} @${t[e].ip} (${t[e].hitCount} hits) ${t[e].abortReason}`);}const n=[];for(const t in e)n.push([t,e[t]]);n.sort(((e,t)=>t[1]-e[1])),Ve$1("// heat:");for(let e=0;e<n.length;e++)Ve$1(`// ${n[e][0]}: ${n[e][1]}`);}else {for(let e=0;e<691;e++){const t=$s(e),n=o$1.mono_jiterp_adjust_abort_count(e,0);n>0?mc[t]=n:delete mc[t];}const e=Object.keys(mc);e.sort(((e,t)=>mc[t]-mc[e]));for(let t=0;t<e.length;t++)Ve$1(`// ${e[t]}: ${mc[e[t]]} abort(s)`);}for(const e in Ms)Ve$1(`// simd ${e}: ${Ms[e]} fallback insn(s)`);}}const Lc="https://dotnet.generated.invalid/interp_pgo";async function Rc(){if(!lt$1.is_runtime_running())return void Ve$1("Skipped saving interp_pgo table (already exited)");const e=await Oc(Lc);if(e)try{const t=o$1.mono_interp_pgo_save_table(0,0);if(t<=0)return void Ve$1("Failed to save interp_pgo table (No data to save)");const r=_$1(t);if(0!==o$1.mono_interp_pgo_save_table(r,t))return void He$1("Failed to save interp_pgo table (Unknown error)");const s=K$1().slice(r,r+t);await async function(e,t,r){try{const r=await Nc();if(!r)return !1;const o=n$1?new Uint8Array(t).slice(0):t,s=new Response(o,{headers:{"content-type":"application/octet-stream","content-length":t.byteLength.toString()}});return await r.put(e,s),!0}catch(t){return We$1("Failed to store entry to the cache: "+e,t),!1}}(e,s)&&Ve$1("Saved interp_pgo table to cache"),async function(e,t){try{const n=await Nc();if(!n)return;const r=await n.keys();for(const o of r)o.url&&o.url!==t&&o.url.startsWith(e)&&await n.delete(o);}catch(e){return}}(Lc,e),m$1(r);}catch(e){He$1(`Failed to save interp_pgo table: ${e}`);}else He$1("Failed to save interp_pgo table (No cache key)");}async function Bc(){const e=await Oc(Lc);if(!e)return void He$1("Failed to create cache key for interp_pgo table");const t=await async function(e){try{const t=await Nc();if(!t)return;const n=await t.match(e);if(!n)return;return n.arrayBuffer()}catch(t){return void We$1("Failed to load entry from the cache: "+e,t)}}(e);if(!t)return void Ve$1("Failed to load interp_pgo table (No table found in cache)");const n=_$1(t.byteLength);K$1().set(new Uint8Array(t),n),o$1.mono_interp_pgo_load_table(n,t.byteLength)&&He$1("Failed to load interp_pgo table (Unknown error)"),m$1(n);}async function Nc(){if(st$1&&false===globalThis.window.isSecureContext)return We$1("Failed to open the cache, running on an insecure origin"),null;if(void 0===globalThis.caches)return We$1("Failed to open the cache, probably running on an insecure origin"),null;const e=`dotnet-resources${document.baseURI.substring(document.location.origin.length)}`;try{return await globalThis.caches.open(e)||null}catch(e){return We$1("Failed to open cache"),null}}async function Oc(t){if(!ct$1.subtle)return null;const n=Object.assign({},ct$1.config);n.resourcesHash=n.resources.hash,delete n.assets,delete n.resources,n.preferredIcuAsset=lt$1.preferredIcuAsset,delete n.forwardConsoleLogsToWS,delete n.diagnosticTracing,delete n.appendElementOnExit,delete n.interopCleanupOnExit,delete n.dumpThreadsOnNonZeroExit,delete n.logExitCode,delete n.pthreadPoolInitialSize,delete n.pthreadPoolUnusedSize,delete n.asyncFlushOnExit,delete n.remoteSources,delete n.ignorePdbLoadErrors,delete n.maxParallelDownloads,delete n.enableDownloadRetry,delete n.extensions,delete n.runtimeId,delete n.jsThreadBlockingMode,n.GitHash=lt$1.gitHash,n.ProductVersion=e$1;const r=JSON.stringify(n),o=await ct$1.subtle.digest("SHA-256",(new TextEncoder).encode(r)),s=new Uint8Array(o);return `${t}-${Array.from(s).map((e=>e.toString(16).padStart(2,"0"))).join("")}`}async function Cc(e){const t=lt$1.config.resources.lazyAssembly;if(!t)throw new Error("No assemblies have been marked as lazy-loadable. Use the 'BlazorWebAssemblyLazyLoad' item group in your project file to enable lazy loading an assembly.");let n=e;e.endsWith(".dll")?n=e.substring(0,e.length-4):e.endsWith(".wasm")&&(n=e.substring(0,e.length-5));const r=n+".dll",o=n+".wasm";let s=null;for(let e=0;e<t.length;e++){const n=t[e];if(n.virtualPath===r||n.virtualPath===o){s=n,s.behavior="assembly";break}}if(null==s)throw new Error(`${e} must be marked with 'BlazorWebAssemblyLazyLoad' item group in your project file to allow lazy-loading.`);if(lt$1.loadedAssemblies.includes(s.name))return  false;const a=n+".pdb";let i=false,c=null;if(0!=lt$1.config.debugLevel&&lt$1.isDebuggingSupported())for(let e=0;e<t.length;e++)if(t[e].virtualPath===a){i=true,c=t[e],c.behavior="pdb";break}const l=lt$1.retrieve_asset_download(s);let p=null,u=null;if(i){const e=null!=c?lt$1.retrieve_asset_download(c):Promise.resolve(null),[t,n]=await Promise.all([l,e]);p=new Uint8Array(t),u=n?new Uint8Array(n):null;}else {const e=await l;p=new Uint8Array(e),u=null;}return function(e,t){lt$1.assert_runtime_running();const n=Ke$1.stackSave();try{const n=$n(4),r=Ln(n,2),o=Ln(n,3);Wn(r,21),Wn(o,21),vo(r,e,4),vo(o,t,4),kn(yn.LoadLazyAssembly,n);}finally{lt$1.is_runtime_running()&&Ke$1.stackRestore(n);}}(p,u),true}async function Dc(e){const t=lt$1.config.resources.satelliteResources;t&&await Promise.all(e.filter((e=>Object.prototype.hasOwnProperty.call(t,e))).map((e=>{const n=[];for(let r=0;r<t[e].length;r++){const o=t[e][r];o.behavior="resource",o.culture=e,n.push(lt$1.retrieve_asset_download(o));}return n})).reduce(((e,t)=>e.concat(t)),new Array).map((async e=>{const t=await e;!function(e){lt$1.assert_runtime_running();const t=Ke$1.stackSave();try{const t=$n(3),n=Ln(t,2);Wn(n,21),vo(n,e,4),kn(yn.LoadSatelliteAssembly,t);}finally{lt$1.is_runtime_running()&&Ke$1.stackRestore(t);}}(new Uint8Array(t));})));}function Fc(e){if(e===l$1)return null;const t=o$1.mono_wasm_read_as_bool_or_null_unsafe(e);return 0!==t&&(1===t||null)}function Pc(e){if(e)try{(e=e.toLocaleLowerCase().replace("_","-")).startsWith("zh-")&&(e=e.replace("-chs","-Hans").replace("-cht","-Hant"));const t=Intl.getCanonicalLocales(e);return t.length>0?t[0]:void 0}catch(e){return}}const Mc=[function(e){Xo&&(globalThis.clearTimeout(Xo),Xo=void 0),Xo=Ke$1.safeSetTimeout(mono_wasm_schedule_timer_tick,e);},function(e,t,n,r,o){if(true!==ct$1.mono_wasm_runtime_is_ready)return;const s=K$1(),a=0!==e?Le$1(e).concat(".dll"):"",i=ht(new Uint8Array(s.buffer,t,n));let c;r&&(c=ht(new Uint8Array(s.buffer,r,o))),Lt({eventName:"AssemblyLoaded",assembly_name:a,assembly_b64:i,pdb_b64:c});},function(e,t){const n=Le$1(t);et$1.logging&&"function"==typeof et$1.logging.debugger&&et$1.logging.debugger(e,n);},function(e,t,n,r){const o={res_ok:e,res:{id:t,value:ht(new Uint8Array(K$1().buffer,n,r))}};bt.has(t)&&We$1(`Adding an id (${t}) that already exists in commands_received`),bt.set(t,o);},function mono_wasm_fire_debugger_agent_message_with_data(e,t){mono_wasm_fire_debugger_agent_message_with_data_to_pause(ht(new Uint8Array(K$1().buffer,e,t)));},mono_wasm_fire_debugger_agent_message_with_data_to_pause,function(){Jo||(Jo=Ke$1.safeSetTimeout(Ko,0));},function(e,t,n,r,s,a,i,c){if(n||mt(false,"expected instruction pointer"),ic||(ic=fa()),!ic.enableTraces)return 1;if(ic.wasmBytesLimit<=ua(6))return 1;if(cc)return 1;let l,p=hc[r];if(p||(hc[r]=p=new dc(n,r,i)),da(0,1),ic.estimateHeat||pc.length>0||p.isVerbose){const e=o$1.mono_wasm_method_get_full_name(t);l=Le$1(e),m$1(e);}const u=Le$1(o$1.mono_wasm_method_get_name(t));p.name=l||u;let d=ic.noExitBackwardBranches?function(e,t,n){const r=t+n,o=[],s=(e-t)/2;for(;e<r;){const n=(e-t)/2,r=O$1(e),a=Ja(e,r);if(271===r);else {const t=gi(e,r);if("number"!=typeof t){e+=2*a;continue}if(0===t){Ve$1(`opcode @${e} branch target is self. aborting backbranch table generation`);break}if(t<0){const r=n+t;if(r<0){Ve$1(`opcode @${e}'s displacement of ${t} goes before body: ${r}. aborting backbranch table generation`);break}r>=s&&o.push(r);}switch(r){case 132:case 133:o.push(n+a);}}e+=2*a;}return o.length<=0?null:new Uint16Array(o)}(n,s,a):null;if(d&&n!==s){const e=(n-s)/2;let t=false;for(let n=0;n<d.length;n++)if(d[n]>=e){t=true;break}t||(d=null);}const f=function(e,t,n,r,s,a,i,c,l){let p=wc;p?p.clear(8):(wc=p=new Cs(8),function(e){e.defineType("trace",{frame:127,pLocals:127,cinfo:127,ip:127},127,true),e.defineType("bailout",{retval:127,base:127,reason:127},127,true),e.defineType("copy_ptr",{dest:127,src:127},64,true),e.defineType("value_copy",{dest:127,src:127,klass:127},64,true),e.defineType("entry",{imethod:127},127,true),e.defineType("strlen",{ppString:127,pResult:127},127,true),e.defineType("getchr",{ppString:127,pIndex:127,pResult:127},127,true),e.defineType("getspan",{destination:127,span:127,index:127,element_size:127},127,true),e.defineType("overflow_check_i4",{lhs:127,rhs:127,opcode:127},127,true),e.defineType("mathop_d_d",{value:124},124,true),e.defineType("mathop_dd_d",{lhs:124,rhs:124},124,true),e.defineType("mathop_f_f",{value:125},125,true),e.defineType("mathop_ff_f",{lhs:125,rhs:125},125,true),e.defineType("fmaf",{x:125,y:125,z:125},125,true),e.defineType("fma",{x:124,y:124,z:124},124,true),e.defineType("trace_eip",{traceId:127,eip:127},64,true),e.defineType("newobj_i",{ppDestination:127,vtable:127},127,true),e.defineType("newstr",{ppDestination:127,length:127},127,true),e.defineType("newarr",{ppDestination:127,vtable:127,length:127},127,true),e.defineType("localloc",{destination:127,len:127,frame:127},64,true),e.defineType("ld_del_ptr",{ppDestination:127,ppSource:127},64,true),e.defineType("ldtsflda",{ppDestination:127,offset:127},64,true),e.defineType("gettype",{destination:127,source:127},127,true),e.defineType("castv2",{destination:127,source:127,klass:127,opcode:127},127,true),e.defineType("hasparent",{klass:127,parent:127},127,true),e.defineType("imp_iface",{vtable:127,klass:127},127,true),e.defineType("imp_iface_s",{obj:127,vtable:127,klass:127},127,true),e.defineType("box",{vtable:127,destination:127,source:127,vt:127},64,true),e.defineType("conv",{destination:127,source:127,opcode:127},127,true),e.defineType("relop_fp",{lhs:124,rhs:124,opcode:127},127,true),e.defineType("safepoint",{frame:127,ip:127},64,true),e.defineType("prof_enter",{frame:127,ip:127},64,true),e.defineType("prof_samplepoint",{frame:127,ip:127},64,true),e.defineType("prof_leave",{frame:127,ip:127},64,true),e.defineType("hashcode",{ppObj:127},127,true),e.defineType("try_hash",{ppObj:127},127,true),e.defineType("hascsize",{ppObj:127},127,true),e.defineType("hasflag",{klass:127,dest:127,sp1:127,sp2:127},64,true),e.defineType("array_rank",{destination:127,source:127},127,true),e.defineType("stfld_o",{locals:127,fieldOffsetBytes:127,targetLocalOffsetBytes:127,sourceLocalOffsetBytes:127},127,true),e.defineType("notnull",{ptr:127,expected:127,traceIp:127,ip:127},64,true),e.defineType("stelemr",{o:127,aindex:127,ref:127},127,true),e.defineType("simd_p_p",{arg0:127,arg1:127},64,true),e.defineType("simd_p_pp",{arg0:127,arg1:127,arg2:127},64,true),e.defineType("simd_p_ppp",{arg0:127,arg1:127,arg2:127,arg3:127},64,true);const t=xc();for(let n=0;n<t.length;n++)t[n]||mt(false,`trace #${n} missing`),e.defineImportedFunction("i",t[n][0],t[n][1],true,t[n][2]);}(p)),ic=p.options;const u=r+s,d=`${t}:${(n-r).toString(16)}`,f=zs();let _=0,m=true,h=false;const g=hc[a],b=g.isVerbose||i&&pc.findIndex((e=>i.indexOf(e)>=0))>=0;b&&!i&&mt(false,"Expected methodFullName if trace is instrumented");const y=b?_c++:0;b&&(Ve$1(`instrumenting: ${i}`),fc[y]=new uc(i)),p.compressImportNames=!b;try{p.appendU32(1836278016),p.appendU32(1),p.generateTypeSection();const t={disp:127,cknull_ptr:127,dest_ptr:127,src_ptr:127,memop_dest:127,memop_src:127,index:127,count:127,math_lhs32:127,math_rhs32:127,math_lhs64:126,math_rhs64:126,temp_f32:125,temp_f64:124};p.options.enableSimd&&(t.v128_zero=123,t.math_lhs128=123,t.math_rhs128=123);let s=!0,i=0;if(p.defineFunction({type:"trace",name:d,export:!0,locals:t},(()=>{switch(p.base=n,p.traceIndex=a,p.frame=e,O$1(n)){case 674:case 675:case 677:case 676:break;default:throw new Error(`Expected *ip to be a jiterpreter opcode but it was ${O$1(n)}`)}return p.cfg.initialize(r,c,b?1:0),i=function(e,t,n,r,s,a,i,c){let l=!0,p=!1,u=!1,d=!1,f=0,_=0,m=0;Ya(),a.backBranchTraceLevel=i?2:0;let h=a.cfg.entry(n);for(;n&&n;){if(a.cfg.ip=n,n>=s){jc(a.traceIndex,0,0,"end-of-body"),i&&Ve$1(`instrumented trace ${t} exited at end of body @${n.toString(16)}`);break}const g=a.options.maxModuleSize-300-a.bytesGeneratedSoFar-a.cfg.overheadBytes;if(a.size>=g){jc(a.traceIndex,0,0,"trace-too-big"),i&&Ve$1(`instrumented trace ${t} exited because of size limit at @${n.toString(16)} (spaceLeft=${g}b)`);break}let b=O$1(n);const y=o$1.mono_jiterp_get_opcode_info(b,2),w=o$1.mono_jiterp_get_opcode_info(b,3),k=Ja(n,b),S=b>=657&&b<=659,v=S?b-657+2:0,U=S?Ca(n,1+v):0;b>=0&&b<691||mt(!1,`invalid opcode ${b}`);const T=S?ga[v][U]:$s(b),E=n,x=a.options.noExitBackwardBranches&&Wa(n,r,c),I=a.branchTargets.has(n),A=x||I||l&&c,j=m+_+a.branchTargets.size;let $=!1,L=ra(b);switch(x&&(a.backBranchTraceLevel>1&&Ve$1(`${t} recording back branch target 0x${n.toString(16)}`),a.backBranchOffsets.push(n)),A&&(u=!1,d=!1,ei(a,n,x),p=!0,Ya(),m=0),L<-1&&p&&(L=-2===L?2:0),l=!1,271===b||(lc.indexOf(b)>=0?(Ws(a,n,23),b=678):u&&(b=678)),b){case 271:ji(a,n,j)||(n=0);break;case 678:u&&(d||a.appendU8(0),d=!0);break;case 313:case 314:ai(a,Ca(n,1),0,Ca(n,2));break;case 312:si(a,Ca(n,1),0),ri(a,Ca(n,2),40),a.local("frame"),a.callImport("localloc");break;case 285:ri(a,Ca(n,1),40),a.i32_const(0),ri(a,Ca(n,2),40),a.appendU8(252),a.appendU8(11),a.appendU8(0);break;case 286:ri(a,Ca(n,1),40),Xs(a,0,Ca(n,2));break;case 310:{const e=Ca(n,3),t=Ca(n,2),r=Ca(n,1),o=Ga(a,e);0!==o&&("number"!=typeof o?(ri(a,e,40),a.local("count",34),a.block(64,4)):(a.i32_const(o),a.local("count",33)),ri(a,r,40),a.local("dest_ptr",34),a.appendU8(69),ri(a,t,40),a.local("src_ptr",34),a.appendU8(69),a.appendU8(114),a.block(64,4),Ws(a,n,2),a.endBlock(),"number"==typeof o&&Qs(a,0,0,o,!1,"dest_ptr","src_ptr")||(a.local("dest_ptr"),a.local("src_ptr"),a.local("count"),a.appendU8(252),a.appendU8(10),a.appendU8(0),a.appendU8(0)),"number"!=typeof o&&a.endBlock());break}case 311:{const e=Ca(n,3),t=Ca(n,2);li(a,Ca(n,1),n,!0),ri(a,t,40),ri(a,e,40),a.appendU8(252),a.appendU8(11),a.appendU8(0);break}case 143:case 145:case 227:case 229:case 144:case 146:case 129:case 132:case 133:bi(a,n,e,b)?p=!0:n=0;break;case 538:{const e=Ca(n,2),t=Ca(n,1);e!==t?(a.local("pLocals"),li(a,e,n,!0),oi(a,t,54)):li(a,e,n,!1),a.allowNullCheckOptimization&&Xa.set(t,n),$=!0;break}case 638:case 639:{const t=P$1(e+ea(4));a.ptr_const(t),a.callImport("entry"),a.block(64,4),Ws(a,n,1),a.endBlock();break}case 676:L=0;break;case 138:break;case 86:{a.local("pLocals");const e=Ca(n,2),r=ci(a,e),o=Ca(n,1),s=Ca(n,3);r||He$1(`${t}: Expected local ${e} to have address taken flag`),si(a,e,s),oi(a,o,54),Ha.set(o,{type:"ldloca",offset:e}),$=!0;break}case 272:case 300:case 301:case 556:{a.local("pLocals");let t=za(e,Ca(n,2));300===b&&(t=o$1.mono_jiterp_imethod_to_ftnptr(t)),a.ptr_const(t),oi(a,Ca(n,1),54);break}case 305:{const t=za(e,Ca(n,3));ri(a,Ca(n,1),40),ri(a,Ca(n,2),40),a.ptr_const(t),a.callImport("value_copy");break}case 306:{const e=Ca(n,3);ri(a,Ca(n,1),40),ri(a,Ca(n,2),40),Ys(a,e);break}case 307:{const e=Ca(n,3);si(a,Ca(n,1),e),li(a,Ca(n,2),n,!0),Ys(a,e);break}case 308:{const t=za(e,Ca(n,3));ri(a,Ca(n,1),40),si(a,Ca(n,2),0),a.ptr_const(t),a.callImport("value_copy");break}case 309:{const e=Ca(n,3);ri(a,Ca(n,1),40),si(a,Ca(n,2),0),Ys(a,e);break}case 540:a.local("pLocals"),li(a,Ca(n,2),n,!0),a.appendU8(40),a.appendMemarg(ea(2),2),oi(a,Ca(n,1),54);break;case 539:{a.block(),ri(a,Ca(n,3),40),a.local("index",34);let e="cknull_ptr";a.options.zeroPageOptimization&&aa()?(da(8,1),ri(a,Ca(n,2),40),e="src_ptr",a.local(e,34)):li(a,Ca(n,2),n,!0),a.appendU8(40),a.appendMemarg(ea(2),2),a.appendU8(72),a.local("index"),a.i32_const(0),a.appendU8(78),a.appendU8(113),a.appendU8(13),a.appendULeb(0),Ws(a,n,11),a.endBlock(),a.local("pLocals"),a.local("index"),a.i32_const(2),a.appendU8(108),a.local(e),a.appendU8(106),a.appendU8(47),a.appendMemarg(ea(3),1),oi(a,Ca(n,1),54);break}case 342:case 343:{const e=Da(n,4);a.block(),ri(a,Ca(n,3),40),a.local("index",34);let t="cknull_ptr";342===b?li(a,Ca(n,2),n,!0):(si(a,Ca(n,2),0),t="src_ptr",a.local(t,34)),a.appendU8(40),a.appendMemarg(ea(7),2),a.appendU8(73),a.local("index"),a.i32_const(0),a.appendU8(78),a.appendU8(113),a.appendU8(13),a.appendULeb(0),Ws(a,n,18),a.endBlock(),a.local("pLocals"),a.local(t),a.appendU8(40),a.appendMemarg(ea(8),2),a.local("index"),a.i32_const(e),a.appendU8(108),a.appendU8(106),oi(a,Ca(n,1),54);break}case 664:a.block(),ri(a,Ca(n,3),40),a.local("count",34),a.i32_const(0),a.appendU8(78),a.appendU8(13),a.appendULeb(0),Ws(a,n,18),a.endBlock(),si(a,Ca(n,1),16),a.local("dest_ptr",34),ri(a,Ca(n,2),40),a.appendU8(54),a.appendMemarg(0,0),a.local("dest_ptr"),a.local("count"),a.appendU8(54),a.appendMemarg(4,0);break;case 577:si(a,Ca(n,1),4),si(a,Ca(n,2),4),a.callImport("ld_del_ptr");break;case 73:si(a,Ca(n,1),4),a.ptr_const(Pa(n,2)),a.callImport("ldtsflda");break;case 663:a.block(),si(a,Ca(n,1),4),si(a,Ca(n,2),0),a.callImport("gettype"),a.appendU8(13),a.appendULeb(0),Ws(a,n,2),a.endBlock();break;case 660:{const t=za(e,Ca(n,4));a.ptr_const(t),si(a,Ca(n,1),4),si(a,Ca(n,2),0),si(a,Ca(n,3),0),a.callImport("hasflag");break}case 669:{const e=ea(1);a.local("pLocals"),li(a,Ca(n,2),n,!0),a.i32_const(e),a.appendU8(106),oi(a,Ca(n,1),54);break}case 661:a.local("pLocals"),si(a,Ca(n,2),0),a.callImport("hashcode"),oi(a,Ca(n,1),54);break;case 662:a.local("pLocals"),si(a,Ca(n,2),0),a.callImport("try_hash"),oi(a,Ca(n,1),54);break;case 665:a.local("pLocals"),si(a,Ca(n,2),0),a.callImport("hascsize"),oi(a,Ca(n,1),54);break;case 670:a.local("pLocals"),ri(a,Ca(n,2),40),a.local("math_lhs32",34),ri(a,Ca(n,3),40),a.appendU8(115),a.i32_const(2),a.appendU8(116),a.local("math_rhs32",33),a.local("math_lhs32"),a.i32_const(327685),a.appendU8(106),a.i32_const(10485920),a.appendU8(114),a.i32_const(1703962),a.appendU8(106),a.i32_const(-8388737),a.appendU8(114),a.local("math_rhs32"),a.appendU8(113),a.appendU8(69),oi(a,Ca(n,1),54);break;case 541:case 542:a.block(),si(a,Ca(n,1),4),si(a,Ca(n,2),0),a.callImport(541===b?"array_rank":"a_elesize"),a.appendU8(13),a.appendULeb(0),Ws(a,n,2),a.endBlock();break;case 289:case 290:{const t=za(e,Ca(n,3)),r=o$1.mono_jiterp_is_special_interface(t),s=289===b,i=Ca(n,1);if(!t){jc(a.traceIndex,0,0,"null-klass"),n=0;continue}a.block(),a.options.zeroPageOptimization&&aa()?(ri(a,Ca(n,2),40),a.local("dest_ptr",34),da(8,1)):(a.block(),ri(a,Ca(n,2),40),a.local("dest_ptr",34),a.appendU8(13),a.appendULeb(0),a.local("pLocals"),a.i32_const(0),oi(a,i,54),a.appendU8(12),a.appendULeb(1),a.endBlock(),a.local("dest_ptr")),r&&a.local("dest_ptr"),a.appendU8(40),a.appendMemarg(ea(14),0),a.ptr_const(t),a.callImport(r?"imp_iface_s":"imp_iface"),s&&(a.local("dest_ptr"),a.appendU8(69),a.appendU8(114)),a.block(64,4),a.local("pLocals"),a.local("dest_ptr"),oi(a,i,54),a.appendU8(5),s?Ws(a,n,19):(a.local("pLocals"),a.i32_const(0),oi(a,i,54)),a.endBlock(),a.endBlock();break}case 291:case 292:case 287:case 288:{const t=za(e,Ca(n,3)),r=291===b||292===b,o=287===b||291===b,s=Ca(n,1);if(!t){jc(a.traceIndex,0,0,"null-klass"),n=0;continue}a.block(),a.options.zeroPageOptimization&&aa()?(ri(a,Ca(n,2),40),a.local("dest_ptr",34),da(8,1)):(a.block(),ri(a,Ca(n,2),40),a.local("dest_ptr",34),a.appendU8(13),a.appendULeb(0),a.local("pLocals"),a.i32_const(0),oi(a,s,54),a.appendU8(12),a.appendULeb(1),a.endBlock(),a.local("dest_ptr")),a.appendU8(40),a.appendMemarg(ea(14),0),a.appendU8(40),a.appendMemarg(ea(15),0),r&&a.local("src_ptr",34),a.i32_const(t),a.appendU8(70),a.block(64,4),a.local("pLocals"),a.local("dest_ptr"),oi(a,s,54),a.appendU8(5),r?(a.local("src_ptr"),a.ptr_const(t),a.callImport("hasparent"),o&&(a.local("dest_ptr"),a.appendU8(69),a.appendU8(114)),a.block(64,4),a.local("pLocals"),a.local("dest_ptr"),oi(a,s,54),a.appendU8(5),o?Ws(a,n,19):(a.local("pLocals"),a.i32_const(0),oi(a,s,54)),a.endBlock()):(si(a,Ca(n,1),4),a.local("dest_ptr"),a.ptr_const(t),a.i32_const(b),a.callImport("castv2"),a.appendU8(69),a.block(64,4),Ws(a,n,19),a.endBlock()),a.endBlock(),a.endBlock();break}case 295:case 296:a.ptr_const(za(e,Ca(n,3))),si(a,Ca(n,1),4),si(a,Ca(n,2),0),a.i32_const(296===b?1:0),a.callImport("box");break;case 299:{const t=za(e,Ca(n,3)),r=ea(17),o=Ca(n,1),s=P$1(t+r);if(!t||!s){jc(a.traceIndex,0,0,"null-klass"),n=0;continue}a.options.zeroPageOptimization&&aa()?(ri(a,Ca(n,2),40),a.local("dest_ptr",34),da(8,1)):(li(a,Ca(n,2),n,!0),a.local("dest_ptr",34)),a.appendU8(40),a.appendMemarg(ea(14),0),a.appendU8(40),a.appendMemarg(ea(15),0),a.local("src_ptr",34),a.appendU8(40),a.appendMemarg(r,0),a.i32_const(s),a.appendU8(70),a.local("src_ptr"),a.appendU8(45),a.appendMemarg(ea(16),0),a.appendU8(69),a.appendU8(113),a.block(64,4),a.local("pLocals"),a.local("dest_ptr"),a.i32_const(ea(18)),a.appendU8(106),oi(a,o,54),a.appendU8(5),Ws(a,n,21),a.endBlock();break}case 294:a.block(),si(a,Ca(n,1),4),ri(a,Ca(n,2),40),a.callImport("newstr"),a.appendU8(13),a.appendULeb(0),Ws(a,n,17),a.endBlock();break;case 293:{a.block(),si(a,Ca(n,1),4);const t=za(e,Ca(n,3));a.i32_const(t),ri(a,Ca(n,2),40),a.callImport("newarr"),a.appendU8(13),a.appendULeb(0),Ws(a,n,17),a.endBlock();break}case 283:a.block(),si(a,Ca(n,1),4),a.ptr_const(za(e,Ca(n,2))),a.callImport("newobj_i"),a.appendU8(13),a.appendULeb(0),Ws(a,n,17),a.endBlock();break;case 282:case 284:case 544:case 543:p?(Hs(a,n,j,15),u=!0,L=0):n=0;break;case 546:case 547:case 548:case 549:case 545:p?(Hs(a,n,j,545==b?22:15),u=!0):n=0;break;case 137:case 134:Ws(a,n,16),u=!0;break;case 130:case 131:Ws(a,n,26),u=!0;break;case 136:if(a.callHandlerReturnAddresses.length>0&&a.callHandlerReturnAddresses.length<=3){const t=Va(e,Ca(n,1));a.local("pLocals"),a.appendU8(40),a.appendMemarg(t,0),a.local("index",33);for(let e=0;e<a.callHandlerReturnAddresses.length;e++){const t=a.callHandlerReturnAddresses[e];a.local("index"),a.ptr_const(t),a.appendU8(70),a.cfg.branch(t,t<n,1);}Ws(a,n,25);}else n=0;break;case 135:case 635:case 636:n=0;break;case 633:case 634:Vs(a,n,b);break;case 493:case 498:case 494:case 496:case 503:case 495:case 502:case 497:a.block(),si(a,Ca(n,1),8),si(a,Ca(n,2),0),a.i32_const(b),a.callImport("conv"),a.appendU8(13),a.appendULeb(0),Ws(a,n,13),a.endBlock();break;case 456:case 457:case 462:case 463:{const e=456===b||462===b,t=462===b||463===b,r=t?0x8000000000000000:2147483648,o=e?"temp_f32":"temp_f64";a.local("pLocals"),ri(a,Ca(n,2),e?42:43),a.local(o,34),a.appendU8(e?139:153),a.appendU8(e?67:68),e?a.appendF32(r):a.appendF64(r),a.appendU8(e?93:99),a.block(t?126:127,4),a.local(o),a.appendU8(ya[b]),a.appendU8(5),a.appendU8(t?66:65),a.appendBoundaryValue(t?64:32,-1),a.endBlock(),oi(a,Ca(n,1),t?55:54);break}case 529:case 530:{const e=529===b;a.local("pLocals"),ri(a,Ca(n,2),e?40:41);const t=Da(n,3),r=Da(n,4);e?a.i32_const(t):a.i52_const(t),a.appendU8(e?106:124),e?a.i32_const(r):a.i52_const(r),a.appendU8(e?108:126),oi(a,Ca(n,1),e?54:55);break}case 650:case 651:{const e=651===b;a.local("pLocals"),ri(a,Ca(n,2),e?41:40),e?a.i52_const(1):a.i32_const(1),a.appendU8(e?132:114),a.appendU8(e?121:103),e&&a.appendU8(167),a.i32_const(e?63:31),a.appendU8(115),oi(a,Ca(n,1),54);break}case 531:case 532:{const e=531===b,t=e?40:41,r=e?54:55;a.local("pLocals"),ri(a,Ca(n,2),t),ri(a,Ca(n,3),t),e?a.i32_const(31):a.i52_const(63),a.appendU8(e?113:131),a.appendU8(e?116:134),oi(a,Ca(n,1),r);break}case 591:case 618:{const e=618===b,t=e?42:43,r=e?56:57;a.local("pLocals"),ri(a,Ca(n,2),t),ri(a,Ca(n,3),t),ri(a,Ca(n,4),t),a.callImport(e?"fmaf":"fma"),oi(a,Ca(n,1),r);break}default:b>=3&&b<=12||b>=509&&b<=510?p||a.options.countBailouts?(Ws(a,n,14),u=!0):n=0:b>=13&&b<=21?pi(a,n,b)?$=!0:n=0:b>=74&&b<=85?ui(a,n,b)||(n=0):b>=344&&b<=427?_i(a,n,b)||(n=0):wa[b]?mi(a,n,b)||(n=0):va[b]?yi(a,n,e,b)?p=!0:n=0:b>=23&&b<=49?di(a,e,n,b)||(n=0):b>=50&&b<=73?fi(a,e,n,b)||(n=0):b>=87&&b<=127?ki(a,n,b)||(n=0):b>=579&&b<=632?wi(a,n,b)||(n=0):b>=315&&b<=341?vi(a,e,n,b)||(n=0):b>=227&&b<=270?a.branchTargets.size>0?(Hs(a,n,j,8),u=!0):n=0:b>=652&&b<=659?(a.containsSimd=!0,Ti(a,n,b,T,v,U)?$=!0:n=0):b>=559&&b<=571?(a.containsAtomics=!0,Ai(a,n,b)||(n=0)):0===L||(n=0);}if(n){if(!$){const e=n+2;for(let t=0;t<w;t++)Za(O$1(e+2*t));}if(ic.dumpTraces||i){let e=`${n.toString(16)} ${T} `;const t=n+2,r=t+2*w;for(let t=0;t<y;t++)0!==t&&(e+=", "),e+=O$1(r+2*t);w>0&&(e+=" -> ");for(let n=0;n<w;n++)0!==n&&(e+=", "),e+=O$1(t+2*n);a.traceBuf.push(e);}L>0&&(p?m++:_++,f+=L),(n+=2*k)<=s&&(h=n);}else i&&Ve$1(`instrumented trace ${t} aborted for opcode ${T} @${E.toString(16)}`),jc(a.traceIndex,0,0,b);}for(;a.activeBlocks>0;)a.endBlock();return a.cfg.exitIp=h,a.containsSimd&&(f+=10240),f}(e,d,n,r,u,p,y,c),s=i>=ic.minimumTraceValue,p.cfg.generate()})),p.emitImportsAndFunctions(!1),!s)return g&&"end-of-body"===g.abortReason&&(g.abortReason="trace-too-small"),0;_=zs();const f=p.getArrayView();if(da(6,f.length),f.length>=p.options.maxModuleSize)return We$1(`Jiterpreter generated too much code (${f.length} bytes) for trace ${d}. Please report this issue.`),0;const h=new WebAssembly.Module(f),w=p.getWasmImports(),k=new WebAssembly.Instance(h,w).exports[d];let S;m=!1,l?(qs().set(l,k),S=l):(S=Gs(0,k),0===S&&(cc=!0));const v=ua(1);return p.options.enableStats&&v&&v%500==0&&$c(!0),S}catch(e){h=true,m=false;let t=p.containsSimd?" (simd)":"";return p.containsAtomics&&(t+=" (atomics)"),He$1(`${i||d}${t} code generation failed: ${e} ${e.stack}`),Zs(),0}finally{const e=zs();if(_?(da(11,_-f),da(12,e-_)):da(11,e-f),h||!m&&ic.dumpTraces||b){if(h||ic.dumpTraces||b)for(let e=0;e<p.traceBuf.length;e++)Ve$1(p.traceBuf[e]);Ve$1(`// ${i||d} generated, blob follows //`);let e="",t=0;try{for(;p.activeBlocks>0;)p.endBlock();p.inSection&&p.endSection();}catch(e){}const n=p.getArrayView(false,true);for(let r=0;r<n.length;r++){const o=n[r];o<16&&(e+="0"),e+=o.toString(16),e+=" ",e.length%10==0&&(Ve$1(`${t}\t${e}`),e="",t=r+1);}Ve$1(`${t}\t${e}`),Ve$1("// end blob //");}}}(e,u,n,s,a,r,l,d,c);return f?(da(1,1),p.fnPtr=f,f):ic.estimateHeat?0:1},function(e){const t=Oi[e&=-2];if(t){if(Di||(Di=fa()),t.hitCount++,t.hitCount===Di.interpEntryFlushThreshold)Pi();else if(t.hitCount!==Di.interpEntryHitCount)return;o$1.mono_jiterp_tlqueue_add(1,e)>=4?Pi():Ni>0||"function"==typeof globalThis.setTimeout&&(Ni=globalThis.setTimeout((()=>{Ni=0,Pi();}),10));}},function(e,t,n,r,o,s,a,i){if(n>16)return 0;const c=new Fi(e,t,n,r,o,s,a,i);Bi||(Bi=qs());const l=Bi.get(i),p=(s?a?29:20:a?11:2)+n;return c.result=Gs(p,l),Oi[e]=c,c.result},function(e,t,n,r,s){const a=P$1(n+0),i=Xi[a];if(i)return void(i.result>0?o$1.mono_jiterp_register_jit_call_thunk(n,i.result):(i.queue.push(n),i.queue.length>12&&Ki()));const c=new Yi(e,t,n,r,0!==s);Xi[a]=c;const l=o$1.mono_jiterp_tlqueue_add(0,e);let p=Qi[e];p||(p=Qi[e]=[]),p.push(c),l>=6&&Ki();},function(e,t,n,r,s){const a=Zi(e);try{a(t,n,r,s);}catch(e){const t=Ke$1.wasmExports.__cpp_exception,n=t instanceof WebAssembly.Tag;if(n&&!(e instanceof WebAssembly.Exception&&e.is(t)))throw e;if(i=s,Ke$1.HEAPU32[i>>>2]=1,n){const n=e.getArg(t,0);o$1.mono_jiterp_begin_catch(n),o$1.mono_jiterp_end_catch();}else {if("number"!=typeof e)throw e;o$1.mono_jiterp_begin_catch(e),o$1.mono_jiterp_end_catch();}}var i;},Ki,function(e,t,n){ct$1.emscriptenBuildOptions.enableDevToolsProfiler&&function(e){Mt.delete(e);}(e),delete hc[n],function(e){delete Oi[e];}(t),function(e){const t=Qi[e];if(t){for(let e=0;e<t.length;e++)delete Xi[t[e].addr];delete Qi[e];}}(e);},function(){return globalThis.performance.now()},function(e,t){const n=st$1?{start:t}:{startTime:t};let r=Mt.get(e);if(!r){const t=a$1.mono_wasm_method_get_name_ex(e);r=Le$1(t),Mt.set(e,r),m$1(t);}globalThis.performance.measure(r,n);},function(e,t,n,r,o){const s=n?Le$1(n):"<no message>",a=!!r,i=e?Le$1(e):"",c=o,l=t?Le$1(t):"",p=`[MONO] ${s}`;if(et$1.logging&&"function"==typeof et$1.logging.trace)et$1.logging.trace(i,l,p,a,c);else switch(l){case "critical":case "error":{const e=p+"\n"+(new Error).stack;lt$1.exitReason||(lt$1.exitReason=e),console.error(Qe$1(e));}break;case "warning":console.warn(p);break;case "message":default:console.log(p);break;case "info":console.info(p);break;case "debug":console.debug(p);}},function(e){wt=lt$1.config.mainAssemblyName+".dll",kt=e,console.assert(true,`Adding an entrypoint breakpoint ${wt} at method token  ${kt}`);debugger},function(e,t){if(!globalThis.crypto||!globalThis.crypto.getRandomValues)return  -1;const n=K$1(),r=n.subarray(e,e+t);(n.buffer,false);const s=r;for(let e=0;e<t;e+=65536){const n=s.subarray(e,e+Math.min(t-e,65536));globalThis.crypto.getRandomValues(n);}return 0},Xc,function(){console.clear();},Mr,function(e){hr(),e=oe$1(e,0);try{return function(e){hr();const t=Ft(),r=Mn(e);2!==r&&mt(!1,`Signature version ${r} mismatch.`);const o=function(e){e||mt(!1,"Null signatures");const t=V$1(e+16);if(0===t)return null;const n=V$1(e+20);return t||mt(!1,"Null name"),Re$1(e+t,e+t+n)}(e),s=function(e){e||mt(!1,"Null signatures");const t=V$1(e+24);return 0===t?null:Re$1(e+t,e+t+V$1(e+28))}(e),a=function(e){return e||mt(!1,"Null signatures"),V$1(e+8)}(e);lt$1.diagnosticTracing&&ze$1(`Binding [JSImport] ${o} from ${s} module`);const i=function(e,t){e&&"string"==typeof e||mt(!1,"function_name must be string");let n={};const r=e.split(".");t?(n=_r.get(t),n||mt(!1,`ES6 module ${t} was not imported yet, please call JSHost.ImportAsync() first in order to invoke ${e}.`)):"INTERNAL"===r[0]?(n=et$1,r.shift()):"globalThis"===r[0]&&(n=globalThis,r.shift());for(let t=0;t<r.length-1;t++){const o=r[t],s=n[o];if(!s)throw new Error(`${o} not found while looking up ${e}`);n=s;}const o=n[r[r.length-1]];if("function"!=typeof o)throw new Error(`${e} must be a Function but was ${typeof o}`);return o.bind(n)}(o,s),c=Pn(e),l=new Array(c),p=new Array(c);let u=!1;for(let t=0;t<c;t++){const n=Bn(e,t+2),r=Nn(n),o=zt(n,r,t+2);o||mt(!1,"ERR42: argument marshaler must be resolved"),l[t]=o,23===r&&(p[t]=e=>{e&&e.dispose();},u=!0);}const d=Bn(e,1),f=Nn(d),_=eo(d,f,1),m=26==f,h=20==f||30==f,g={fn:i,fqn:s+":"+o,args_count:c,arg_marshalers:l,res_converter:_,has_cleanup:u,arg_cleanup:p,is_discard_no_wait:m,is_async:h,isDisposed:!1};let b;b=h||m||u?ar(g):0!=c||_?1!=c||_?1==c&&_?function(e){const t=e.fn,r=e.arg_marshalers[0],o=e.res_converter,s=e.fqn;return e=null,function(a){const i=Ft();try{n$1&&e.isDisposed;const s=r(a),i=t(s);o(a,i);}catch(e){wo(a,e);}finally{Pt(i,"mono.callCsFunction:",s);}}}(g):2==c&&_?function(e){const t=e.fn,r=e.arg_marshalers[0],o=e.arg_marshalers[1],s=e.res_converter,a=e.fqn;return e=null,function(i){const c=Ft();try{n$1&&e.isDisposed;const a=r(i),c=o(i),l=t(a,c);s(i,l);}catch(e){wo(i,e);}finally{Pt(c,"mono.callCsFunction:",a);}}}(g):ar(g):function(e){const t=e.fn,r=e.arg_marshalers[0],o=e.fqn;return e=null,function(s){const a=Ft();try{n$1&&e.isDisposed;const o=r(s);t(o);}catch(e){wo(s,e);}finally{Pt(a,"mono.callCsFunction:",o);}}}(g):function(e){const t=e.fn,r=e.fqn;return e=null,function(o){const s=Ft();try{n$1&&e.isDisposed,t();}catch(e){wo(o,e);}finally{Pt(s,"mono.callCsFunction:",r);}}}(g);let y=b;y[xn]=g,sr[a]=y,Pt(t,"mono.bindJsFunction:",o);}(e),0}catch(e){return Ne$1(function(e){let t="unknown exception";if(e){t=e.toString();const n=e.stack;n&&(n.startsWith(t)?t=n:t+="\n"+n),t=Xe$1(t);}return t}(e))}},function(e,t){!function(e,t){lt$1.assert_runtime_running();const n=Fr(e);n&&"function"==typeof n&&n[En]||mt(false,`Bound function handle expected ${e}`),n(t=oe$1(t,0));}(e,t);},function(e,t){lt$1.assert_runtime_running(),t=oe$1(t,0);const n=sr[e];n||mt(false,`Imported function handle expected ${e}`),n(t);},function(e){gr((()=>function(e){if(!lt$1.is_runtime_running())return void(lt$1.diagnosticTracing&&ze$1("This promise resolution/rejection can't be propagated to managed code, mono runtime already exited."));const t=Ln(e=oe$1(e,0),0),r=n$1;try{lt$1.assert_runtime_running();const n=Ln(e,1),o=Ln(e,2),s=Ln(e,3),a=zn(o),i=Qn(o),c=Fr(i);c||mt(!1,`Cannot find Promise for JSHandle ${i}`),c.resolve_or_reject(a,i,s),r||(Wn(n,1),Wn(t,0));}catch(e){wo(t,e);}}(e)));},function(e){gr((()=>function(e){if(!lt$1.is_runtime_running())return void(lt$1.diagnosticTracing&&ze$1("This promise can't be canceled, mono runtime already exited."));const t=qr(e);t||mt(false,`Expected Promise for GCHandle ${e}`),t.cancel();}(e)));},function(e,t,n,r,o,s,a){try{const i=Re$1(n,n+2*r),c=Pc(i);if(!c&&i)return Be$1(o,o+2*i.length,i),T$1(a,i.length),0;const l=Pc(Re$1(e,e+2*t));if(!c||!l)throw new Error(`Locale or culture name is null or empty. localeName=${c}, cultureName=${l}`);const p=c.split("-");let u,d;try{const e=p.length>1?p.pop():void 0;d=e?new Intl.DisplayNames([l],{type:"region"}).of(e):void 0;const t=p.join("-");u=new Intl.DisplayNames([l],{type:"language"}).of(t);}catch(e){if(!(e instanceof RangeError))throw e;try{u=new Intl.DisplayNames([l],{type:"language"}).of(c);}catch(e){if(e instanceof RangeError&&i)return Be$1(o,o+2*i.length,i),T$1(a,i.length),0;throw e}}const f={LanguageName:u,RegionName:d},_=Object.values(f).join("##");if(!_)throw new Error(`Locale info for locale=${c} is null or empty.`);if(_.length>s)throw new Error(`Locale info for locale=${c} exceeds length of ${s}.`);return Be$1(o,o+2*_.length,_),T$1(a,_.length),0}catch(e){return T$1(a,-1),Ne$1(e.toString())}},function(e){return pt.ds_rt_websocket_create(e)},function(e,t,n){return pt.ds_rt_websocket_send(e,t,n)},function(e){return pt.ds_rt_websocket_poll(e)},function(e,t,n){return pt.ds_rt_websocket_recv(e,t,n)},function(e){return pt.ds_rt_websocket_close(e)}];async function zc(e,t){try{const n=await Vc(e,t);return lt$1.mono_exit(n),n}catch(e){try{lt$1.mono_exit(1,e);}catch(e){}return e&&"number"==typeof e.status?e.status:1}}async function Vc(e,t){null!=e&&""!==e||(e=lt$1.config.mainAssemblyName)||mt(false,"Null or empty config.mainAssemblyName"),null==t&&(t=ct$1.config.applicationArguments),null==t&&(t=tt$1?(await import(/*! webpackIgnore: true */'process')).argv.slice(2):[]),function(e,t){const n=t.length+1,r=_$1(4*n);let s=0;Ke$1.setValue(r+4*s,o$1.mono_wasm_strdup(e),"i32"),s+=1;for(let e=0;e<t.length;++e)Ke$1.setValue(r+4*s,o$1.mono_wasm_strdup(t[e]),"i32"),s+=1;o$1.mono_wasm_set_main_args(n,r);}(e,t),lt$1.config.mainAssemblyName=e,-1==ct$1.waitForDebugger&&(Ve$1("waiting for debugger..."),await new Promise((e=>{const t=setInterval((()=>{1==ct$1.waitForDebugger&&(clearInterval(t),e());}),100);})));try{return Ke$1.runtimeKeepalivePush(),await new Promise((e=>globalThis.setTimeout(e,0))),await function(e,t,n){lt$1.assert_runtime_running();const r=Ke$1.stackSave();try{const r=$n(5),o=Ln(r,1),s=Ln(r,2),a=Ln(r,3),i=Ln(r,4);uo(s,$e$1(e)),Uo(a,t&&!t.length?void 0:t,15),no(i,n);let c=sn(o,0,Jt);return wn(ct$1.managedThreadTID,yn.CallEntrypoint,r),c=an(r,Jt,c),null==c&&(c=Promise.resolve(0)),c[Dr]=!0,c}finally{lt$1.is_runtime_running()&&Ke$1.stackRestore(r);}}(e,t,1==ct$1.waitForDebugger)}finally{Ke$1.runtimeKeepalivePop();}}function Wc(e){ct$1.runtimeReady&&(ct$1.runtimeReady=false,o$1.mono_wasm_exit(e));}function Hc(e){if(lt$1.exitReason=e,ct$1.runtimeReady){ct$1.runtimeReady=false;const t=Qe$1(e);Ke$1.abort(t);}throw e}var qc,Gc;const Jc=2147483647&(null!==(Gc=null===(qc=globalThis.performance)||void 0===qc?void 0:qc.timeOrigin)&&void 0!==Gc?Gc:Date.now());function Xc(){return Jc}async function Qc(e){e.out||(e.out=console.log.bind(console)),e.err||(e.err=console.error.bind(console)),e.print||(e.print=e.out),e.printErr||(e.printErr=e.err),lt$1.out=e.print,lt$1.err=e.printErr,await async function(){var e;if(tt$1){if(globalThis.performance===Io){const{performance:e}=et$1.require("perf_hooks");globalThis.performance=e;}if(et$1.process=await import(/*! webpackIgnore: true */'process'),globalThis.crypto||(globalThis.crypto={}),!globalThis.crypto.getRandomValues){let e;try{e=et$1.require("node:crypto");}catch(e){}e?e.webcrypto?globalThis.crypto=e.webcrypto:e.randomBytes&&(globalThis.crypto.getRandomValues=t=>{t&&t.set(e.randomBytes(t.length));}):globalThis.crypto.getRandomValues=()=>{throw new Error("Using node without crypto support. To enable current operation, either provide polyfill for 'globalThis.crypto.getRandomValues' or enable 'node:crypto' module.")};}}ct$1.subtle=null===(e=globalThis.crypto)||void 0===e?void 0:e.subtle;}();}function Yc(e){const t=Ft();e.locateFile||(e.locateFile=e.__locateFile=e=>lt$1.scriptDirectory+e),e.mainScriptUrlOrBlob=lt$1.scriptUrl;const s=e.instantiateWasm,i=e.preInit?"function"==typeof e.preInit?[e.preInit]:e.preInit:[],l=e.preRun?"function"==typeof e.preRun?[e.preRun]:e.preRun:[],p=e.postRun?"function"==typeof e.postRun?[e.postRun]:e.postRun:[],u=e.onRuntimeInitialized?e.onRuntimeInitialized:()=>{};e.instantiateWasm=(e,t)=>function(e,t,n){const r=Ft();if(n){const o=n(e,((e,n)=>{Pt(r,"mono.instantiateWasm"),ct$1.afterInstantiateWasm.promise_control.resolve(),t(e,n);}));return o}return async function(e,t){try{await lt$1.afterConfigLoaded,lt$1.diagnosticTracing&&ze$1("instantiate_wasm_module"),await ct$1.beforePreInit.promise,Ke$1.addRunDependency("instantiate_wasm_module"),await async function(){const e=lt$1.simd(),t=lt$1.relaxedSimd(),n=lt$1.exceptions();ct$1.featureWasmSimd=await e,ct$1.featureWasmRelaxedSimd=await t,ct$1.featureWasmEh=await n,ct$1.emscriptenBuildOptions.wasmEnableSIMD&&(ct$1.featureWasmSimd||mt(!1,"This browser/engine doesn't support WASM SIMD. Please use a modern version. See also https://aka.ms/dotnet-wasm-features")),ct$1.emscriptenBuildOptions.wasmEnableEH&&(ct$1.featureWasmEh||mt(!1,"This browser/engine doesn't support WASM exception handling. Please use a modern version. See also https://aka.ms/dotnet-wasm-features"));}(),function(e){const t=e.env||e.a;if(!t)return void We$1("WARNING: Neither imports.env or imports.a were present when instantiating the wasm module. This likely indicates an emscripten configuration issue.");const n=new Array(Mc.length);for(const e in t){const r=t[e];if("function"==typeof r&&-1!==r.toString().indexOf("runtime_idx"))try{const{runtime_idx:t}=r();if(void 0!==n[t])throw new Error(`Duplicate runtime_idx ${t}`);n[t]=e;}catch(e){}}for(const[e,r]of Mc.entries()){const o=n[e];if(void 0!==o){if("function"!=typeof t[o])throw new Error(`Expected ${o} to be a function`);t[o]=r;}}}(e);const n=await lt$1.wasmCompilePromise.promise;t(await WebAssembly.instantiate(n,e),n),lt$1.diagnosticTracing&&ze$1("instantiate_wasm_module done"),ct$1.afterInstantiateWasm.promise_control.resolve();}catch(e){throw He$1("instantiate_wasm_module() failed",e),lt$1.mono_exit(1,e),e}Ke$1.removeRunDependency("instantiate_wasm_module");}(e,t),[]}(e,t,s),e.preInit=[()=>function(e){Ke$1.addRunDependency("mono_pre_init");const t=Ft();try{Ke$1.addRunDependency("mono_wasm_pre_init_essential"),lt$1.diagnosticTracing&&ze$1("mono_wasm_pre_init_essential"),lt$1.gitHash!==ct$1.gitHash&&We$1(`The version of dotnet.runtime.js ${ct$1.gitHash} is different from the version of dotnet.js ${lt$1.gitHash}!`),lt$1.gitHash!==ct$1.emscriptenBuildOptions.gitHash&&We$1(`The version of dotnet.native.js ${ct$1.emscriptenBuildOptions.gitHash}  is different from the version of dotnet.js ${lt$1.gitHash}!`),n$1!==ct$1.emscriptenBuildOptions.wasmEnableThreads&&We$1(`The threads of dotnet.native.js ${ct$1.emscriptenBuildOptions.wasmEnableThreads} is different from the version of dotnet.runtime.js ${n$1}!`),function(){const e=[...r$1];for(const t of e){const e=o$1,[n,r,s,a,i]=t,l="function"==typeof n;if(!0===n||l)e[r]=function(...t){!l||!n()||mt(!1,`cwrap ${r} should not be called when binding was skipped`);const o=c$1(r,s,a,i);return e[r]=o,o(...t)};else {const t=c$1(r,s,a,i);e[r]=t;}}}(),s=et$1,Object.assign(s,{mono_wasm_exit:o$1.mono_wasm_exit,mono_wasm_profiler_init_aot:a$1.mono_wasm_profiler_init_aot,mono_wasm_profiler_init_browser_devtools:a$1.mono_wasm_profiler_init_browser_devtools,mono_wasm_exec_regression:o$1.mono_wasm_exec_regression,mono_wasm_print_thread_dump:void 0}),Ke$1.removeRunDependency("mono_wasm_pre_init_essential"),lt$1.diagnosticTracing&&ze$1("preInit"),ct$1.beforePreInit.promise_control.resolve(),e.forEach((e=>e(Ke$1)));}catch(e){throw He$1("user preInint() failed",e),lt$1.mono_exit(1,e),e}var s;(async()=>{try{await async function(){lt$1.diagnosticTracing&&ze$1("mono_wasm_pre_init_essential_async"),Ke$1.addRunDependency("mono_wasm_pre_init_essential_async"),Ke$1.removeRunDependency("mono_wasm_pre_init_essential_async");}(),Pt(t,"mono.preInit");}catch(e){throw lt$1.mono_exit(1,e),e}ct$1.afterPreInit.promise_control.resolve(),Ke$1.removeRunDependency("mono_pre_init");})();}(i)],e.preRun=[()=>async function(e){Ke$1.addRunDependency("mono_pre_run_async");try{await ct$1.afterInstantiateWasm.promise,await ct$1.afterPreInit.promise,lt$1.diagnosticTracing&&ze$1("preRunAsync");const t=Ft();e.map((e=>e(Ke$1))),Pt(t,"mono.preRun");}catch(e){throw He$1("preRunAsync() failed",e),lt$1.mono_exit(1,e),e}ct$1.afterPreRun.promise_control.resolve(),Ke$1.removeRunDependency("mono_pre_run_async");}(l)],e.onRuntimeInitialized=()=>async function(e){try{await ct$1.afterPreRun.promise,lt$1.diagnosticTracing&&ze$1("onRuntimeInitialized"),ct$1.nativeExit=Wc,ct$1.nativeAbort=Hc;const t=Ft();if(ct$1.beforeOnRuntimeInitialized.promise_control.resolve(),await ct$1.coreAssetsInMemory.promise,ct$1.config.virtualWorkingDirectory){const e=Ke$1.FS,t=ct$1.config.virtualWorkingDirectory;try{const n=e.stat(t);n?n&&e.isDir(n.mode)||mt(!1,`FS.chdir: ${t} is not a directory`):Ke$1.FS_createPath("/",t,!0,!0);}catch(e){Ke$1.FS_createPath("/",t,!0,!0);}e.chdir(t);}ct$1.config.interpreterPgo&&setTimeout(Kc,1e3*(ct$1.config.interpreterPgoSaveDelay||15)),Ke$1.runtimeKeepalivePush(),n$1||await async function(){try{const t=Ft(),n=ct$1.config.environmentVariables||{};lt$1.diagnosticTracing&&ze$1("Initializing mono runtime");for(const e in n){const t=n[e];if("string"!=typeof t)throw new Error(`Expected environment variable '${e}' to be a string but it was ${typeof t}: '${t}'`);Zc(e,t);}if(ct$1.config.runtimeOptions&&function(e){if(!Array.isArray(e))throw new Error("Expected runtimeOptions to be an array of strings");const t=_$1(4*e.length);let n=0;for(let r=0;r<e.length;++r){const s=e[r];if("string"!=typeof s)throw new Error("Expected runtimeOptions to be an array of strings");Ke$1.setValue(t+4*n,o$1.mono_wasm_strdup(s),"i32"),n+=1;}o$1.mono_wasm_parse_runtime_options(e.length,t);}(ct$1.config.runtimeOptions),ct$1.emscriptenBuildOptions.enableEventPipe){const e="DOTNET_DiagnosticPorts",t="js://ready";n[e]||(n[e]=t,Zc(e,t));}else ct$1.emscriptenBuildOptions.enableAotProfiler?function(e){ct$1.emscriptenBuildOptions.enableAotProfiler||mt(!1,"AOT profiler is not enabled, please use <WasmProfilers>aot;</WasmProfilers> in your project file."),null==e&&(e={}),"writeAt"in e||(e.writeAt="System.Runtime.InteropServices.JavaScript.JavaScriptExports::StopProfile"),"sendTo"in e||(e.sendTo="Interop/Runtime::DumpAotProfileData");const t="aot:write-at-method="+e.writeAt+",send-to-method="+e.sendTo;a$1.mono_wasm_profiler_init_aot(t);}(ct$1.config.aotProfilerOptions||{}):ct$1.emscriptenBuildOptions.enableDevToolsProfiler?function(){ct$1.emscriptenBuildOptions.enableDevToolsProfiler||mt(!1,"DevTools profiler is not enabled, please use <WasmProfilers>browser:callspec=N:Sample</WasmProfilers> in your project file."),Dt=globalThis.performance&&"function"==typeof globalThis.performance.measure;const e=`${ct$1.config.environmentVariables.DOTNET_WasmPerformanceInstrumentation||"callspec=all"}`;a$1.mono_wasm_profiler_init_browser_devtools(e);}():ct$1.emscriptenBuildOptions.enableLogProfiler&&(e=ct$1.config.logProfilerOptions||{},ct$1.emscriptenBuildOptions.enableLogProfiler||mt(!1,"Log profiler is not enabled, please use <WasmProfilers>log;</WasmProfilers> in your project file."),e.takeHeapshot||mt(!1,"Log profiler is not enabled, the takeHeapshot method must be defined in LogProfilerOptions.takeHeapshot"),e.configuration||(e.configuration="log:alloc,output=output.mlpd"),e.takeHeapshot?a$1.mono_wasm_profiler_init_log(`${e.configuration},take-heapshot-method=${e.takeHeapshot}`):a$1.mono_wasm_profiler_init_log(e.configuration));!function(){var e,t,n,r;lt$1.diagnosticTracing&&ze$1("mono_wasm_load_runtime");try{const s=Ft();let a=ct$1.config.debugLevel;null==a&&(a=0,ct$1.config.debugLevel&&(a=0+a)),lt$1.isDebuggingSupported()&&(ct$1.config.resources.corePdb||ct$1.config.resources.pdb)||(a=0);const i=new Map;if(null===(t=null===(e=ct$1.config.runtimeConfig)||void 0===e?void 0:e.runtimeOptions)||void 0===t?void 0:t.configProperties)for(const[e,t]of Object.entries(null===(r=null===(n=ct$1.config.runtimeConfig)||void 0===n?void 0:n.runtimeOptions)||void 0===r?void 0:r.configProperties))i.set(e,""+t);i.set("APP_CONTEXT_BASE_DIRECTORY","/"),i.set("RUNTIME_IDENTIFIER","browser-wasm");const c=i.size,l=[],p=_$1(4*i.size),u=_$1(4*i.size);l.push(p),l.push(u);let d=0;for(const[e,t]of i.entries()){const n=$e$1(e),r=$e$1(t);S$1(p+4*d,n),S$1(u+4*d,r),d++,l.push(n),l.push(r);}o$1.mono_wasm_load_runtime(a,c,p,u);for(const e of l)Ke$1._free(e);Pt(s,"mono.loadRuntime");}catch(e){throw He$1("mono_wasm_load_runtime () failed",e),lt$1.mono_exit(1,e),e}}(),function(){if(ma)return;ma=!0;const e=fa(),t=e.tableSize,n=ct$1.emscriptenBuildOptions.runAOTCompilation?e.tableSize:1,r=ct$1.emscriptenBuildOptions.runAOTCompilation?e.aotTableSize:1,s=t+n+36*r+1,a=qs();let i=a.length;const c=performance.now();a.grow(s);const l=performance.now();e.enableStats&&Ve$1(`Allocated ${s} function table entries for jiterpreter, bringing total table size to ${a.length}`),i=_a(0,i,t,ta("mono_jiterp_placeholder_trace")),i=_a(1,i,n,ta("mono_jiterp_placeholder_jit_call"));for(let e=2;e<=37;e++)i=_a(e,i,r,a.get(o$1.mono_jiterp_get_interp_entry_func(e)));const p=performance.now();e.enableStats&&Ve$1(`Growing wasm function table took ${l-c}. Filling table took ${p-l}.`);}(),function(){if(!ct$1.mono_wasm_bindings_is_ready){lt$1.diagnosticTracing&&ze$1("bindings_init"),ct$1.mono_wasm_bindings_is_ready=!0;try{const e=Ft();we$1||("undefined"!=typeof TextDecoder&&(Se$1=new TextDecoder("utf-16le"),ve$1=new TextDecoder("utf-8",{fatal:!1}),Ue$1=new TextDecoder("utf-8"),Te$1=new TextEncoder),we$1=_$1(12)),Ee$1||(Ee$1=function(e){let t;if(de$1.length>0)t=de$1.pop();else {const e=function(){if(null==le$1||!pe$1){le$1=_e$1(ce$1,"js roots"),pe$1=new Int32Array(ce$1),ue$1=ce$1;for(let e=0;e<ce$1;e++)pe$1[e]=ce$1-e-1;}if(ue$1<1)throw new Error("Out of scratch root space");const e=pe$1[ue$1-1];return ue$1--,e}();t=new he$1(le$1,e);}if(void 0!==e);else t.set(0);return t}()),function(){const e="System.Runtime.InteropServices.JavaScript";if(ct$1.runtime_interop_module=o$1.mono_wasm_assembly_load(e),!ct$1.runtime_interop_module)throw "Can't find bindings module assembly: "+e;if(ct$1.runtime_interop_namespace=e,ct$1.runtime_interop_exports_classname="JavaScriptExports",ct$1.runtime_interop_exports_class=o$1.mono_wasm_assembly_find_class(ct$1.runtime_interop_module,ct$1.runtime_interop_namespace,ct$1.runtime_interop_exports_classname),!ct$1.runtime_interop_exports_class)throw "Can't find "+ct$1.runtime_interop_namespace+"."+ct$1.runtime_interop_exports_classname+" class";yn.InstallMainSynchronizationContext=void 0,yn.CallEntrypoint=Sn("CallEntrypoint"),yn.BindAssemblyExports=Sn("BindAssemblyExports"),yn.ReleaseJSOwnedObjectByGCHandle=Sn("ReleaseJSOwnedObjectByGCHandle"),yn.CompleteTask=Sn("CompleteTask"),yn.CallDelegate=Sn("CallDelegate"),yn.GetManagedStackTrace=Sn("GetManagedStackTrace"),yn.LoadSatelliteAssembly=Sn("LoadSatelliteAssembly"),yn.LoadLazyAssembly=Sn("LoadLazyAssembly");}(),0==vn.size&&(vn.set(21,_n),vn.set(23,hn),vn.set(22,gn),vn.set(3,Wt),vn.set(4,Ht),vn.set(5,qt),vn.set(6,Gt),vn.set(7,Jt),vn.set(8,Xt),vn.set(9,Qt),vn.set(11,Yt),vn.set(12,Kt),vn.set(10,Zt),vn.set(15,pn),vn.set(16,un),vn.set(27,un),vn.set(13,dn),vn.set(14,fn),vn.set(17,tn),vn.set(18,tn),vn.set(20,on),vn.set(29,on),vn.set(28,on),vn.set(30,sn),vn.set(24,nn),vn.set(25,nn),vn.set(0,en),vn.set(1,en),vn.set(2,en),vn.set(26,en)),0==Un.size&&(Un.set(21,vo),Un.set(23,To),Un.set(22,Eo),Un.set(3,no),Un.set(4,ro),Un.set(5,oo),Un.set(6,so),Un.set(7,ao),Un.set(8,io),Un.set(9,co),Un.set(10,lo),Un.set(11,po),Un.set(12,uo),Un.set(17,fo),Un.set(18,_o),Un.set(15,mo),Un.set(16,wo),Un.set(27,wo),Un.set(13,ko),Un.set(14,So),Un.set(20,yo),Un.set(28,yo),Un.set(29,yo),Un.set(24,bo),Un.set(25,bo),Un.set(0,go),Un.set(2,go),Un.set(1,go),Un.set(26,go)),ct$1._i52_error_scratch_buffer=_$1(4),Pt(e,"mono.bindingsInit");}catch(e){throw He$1("Error in bindings_init",e),e}}}(),ct$1.runtimeReady=!0,ct$1.afterMonoStarted.promise_control.resolve(),ct$1.config.interpreterPgo&&await Bc(),Pt(t,"mono.startRuntime");}catch(e){throw He$1("start_runtime() failed",e),lt$1.mono_exit(1,e),e}var e;}(),await async function(){await ct$1.allAssetsInMemory.promise,ct$1.config.assets&&(lt$1.actual_downloaded_assets_count!=lt$1.expected_downloaded_assets_count&&mt(!1,`Expected ${lt$1.expected_downloaded_assets_count} assets to be downloaded, but only finished ${lt$1.actual_downloaded_assets_count}`),lt$1.actual_instantiated_assets_count!=lt$1.expected_instantiated_assets_count&&mt(!1,`Expected ${lt$1.expected_instantiated_assets_count} assets to be in memory, but only instantiated ${lt$1.actual_instantiated_assets_count}`),lt$1._loaded_files.forEach((e=>lt$1.loadedFiles.push(e.url))),lt$1.diagnosticTracing&&ze$1("all assets are loaded in wasm memory"));}(),tl.registerRuntime(it$1),ct$1.mono_wasm_runtime_is_ready||function mono_wasm_runtime_ready(){if(et$1.mono_wasm_runtime_is_ready=ct$1.mono_wasm_runtime_is_ready=!0,vt=0,St={},Ut=-1,globalThis.dotnetDebugger)debugger}();try{e(Ke$1);}catch(e){throw He$1("user callback onRuntimeInitialized() failed",e),e}await async function(){lt$1.diagnosticTracing&&ze$1("mono_wasm_after_user_runtime_initialized");try{if(Ke$1.onDotnetReady)try{await Ke$1.onDotnetReady();}catch(e){throw He$1("onDotnetReady () failed",e),e}}catch(e){throw He$1("mono_wasm_after_user_runtime_initialized () failed",e),e}}(),Pt(t,"mono.onRuntimeInitialized");}catch(e){throw Ke$1.runtimeKeepalivePop(),He$1("onRuntimeInitializedAsync() failed",e),lt$1.mono_exit(1,e),e}ct$1.afterOnRuntimeInitialized.promise_control.resolve();}(u),e.postRun=[()=>async function(e){try{await ct$1.afterOnRuntimeInitialized.promise,lt$1.diagnosticTracing&&ze$1("postRunAsync");const t=Ft();Ke$1.FS_createPath("/","usr",!0,!0),Ke$1.FS_createPath("/","usr/share",!0,!0),e.map((e=>e(Ke$1))),Pt(t,"mono.postRun");}catch(e){throw He$1("postRunAsync() failed",e),lt$1.mono_exit(1,e),e}ct$1.afterPostRun.promise_control.resolve();}(p)],e.ready.then((async()=>{await ct$1.afterPostRun.promise,Pt(t,"mono.emscriptenStartup"),ct$1.dotnetReady.promise_control.resolve(it$1);})).catch((e=>{ct$1.dotnetReady.promise_control.reject(e);})),e.ready=ct$1.dotnetReady.promise;}function Zc(e,t){o$1.mono_wasm_setenv(e,t);}async function Kc(){ void 0!==lt$1.exitCode&&0!==lt$1.exitCode||await Rc();}async function el(e){}let tl;function nl(r){const o=Ke$1,a=r,i=globalThis;Object.assign(a.internal,{mono_wasm_exit:e=>{Ke$1.err("early exit "+e);},forceDisposeProxies:Jr,mono_wasm_dump_threads:void 0,logging:void 0,mono_wasm_stringify_as_error_with_stack:Qe$1,mono_wasm_get_loaded_files:As,mono_wasm_send_dbg_command_with_parms:Et,mono_wasm_send_dbg_command:xt,mono_wasm_get_dbg_command_info:It,mono_wasm_get_details:Nt,mono_wasm_release_object:Ct,mono_wasm_call_function_on:Bt,mono_wasm_debugger_resume:At,mono_wasm_detach_debugger:jt,mono_wasm_raise_debug_event:Lt,mono_wasm_change_debugger_log_level:$t,mono_wasm_debugger_attached:Rt,mono_wasm_runtime_is_ready:ct$1.mono_wasm_runtime_is_ready,mono_wasm_get_func_id_to_name_mappings:Ze$1,get_property:lr,set_property:cr,has_property:pr,get_typeof_property:ur,get_global_this:dr,get_dotnet_instance:()=>it$1,dynamic_import:mr,mono_wasm_bind_cs_function:wr,ws_wasm_create:bs,ws_wasm_open:ys,ws_wasm_send:ws,ws_wasm_receive:ks,ws_wasm_close:Ss,ws_wasm_abort:vs,ws_get_state:gs,http_wasm_supports_streaming_request:$o,http_wasm_supports_streaming_response:Lo,http_wasm_create_controller:Ro,http_wasm_get_response_type:Mo,http_wasm_get_response_status:zo,http_wasm_abort:No,http_wasm_transform_stream_write:Oo,http_wasm_transform_stream_close:Co,http_wasm_fetch:Po,http_wasm_fetch_stream:Do,http_wasm_fetch_bytes:Fo,http_wasm_get_response_header_names:Vo,http_wasm_get_response_header_values:Wo,http_wasm_get_response_bytes:qo,http_wasm_get_response_length:Ho,http_wasm_get_streamed_response_bytes:Go,jiterpreter_dump_stats:$c,jiterpreter_apply_options:pa,jiterpreter_get_options:fa,interp_pgo_load_data:Bc,interp_pgo_save_data:Rc,mono_wasm_gc_lock:ae$1,mono_wasm_gc_unlock:ie$1,monoObjectAsBoolOrNullUnsafe:Fc,monoStringToStringUnsafe:Pe$1,loadLazyAssembly:Cc,loadSatelliteAssemblies:Dc});const c={stringify_as_error_with_stack:Qe$1,instantiate_symbols_asset:Is,instantiate_asset:xs,jiterpreter_dump_stats:$c,forceDisposeProxies:Jr,utf8ToString:Le$1,mono_wasm_process_current_pid:Xc,mono_background_exec:()=>s$1.mono_background_exec(),mono_wasm_ds_exec:()=>s$1.mono_wasm_ds_exec()};Object.assign(ct$1,c);const l={runMain:Vc,runMainAndExit:zc,exit:lt$1.mono_exit,setEnvironmentVariable:Zc,getAssemblyExports:vr,setModuleImports:ir,getConfig:()=>ct$1.config,invokeLibraryInitializers:lt$1.invokeLibraryInitializers,setHeapB32:b$1,setHeapB8:y$1,setHeapU8:w$1,setHeapU16:k$1,setHeapU32:S$1,setHeapI8:v$1,setHeapI16:U$1,setHeapI32:T$1,setHeapI52:x$1,setHeapU52:I$1,setHeapI64Big:A$1,setHeapF32:j$1,setHeapF64:$$1,getHeapB32:R$1,getHeapB8:B$1,getHeapU8:N$1,getHeapU16:O$1,getHeapU32:C$1,getHeapI8:M$1,getHeapI16:z$1,getHeapI32:V$1,getHeapI52:W$1,getHeapU52:H$1,getHeapI64Big:q$1,getHeapF32:G$1,getHeapF64:J$1,localHeapViewU8:K$1,localHeapViewU16:ee$1,localHeapViewU32:te$1,localHeapViewI8:X$1,localHeapViewI16:Q$1,localHeapViewI32:Y$1,localHeapViewI64Big:Z$1,localHeapViewF32:ne$1,localHeapViewF64:re$1,collectCpuSamples:null,collectMetrics:null,collectGcDump:null,connectDSRouter:null};return Object.assign(it$1,{INTERNAL:a.internal,Module:o,runtimeBuildInfo:{productVersion:e$1,gitHash:ct$1.gitHash,buildConfiguration:t$1,wasmEnableThreads:n$1,wasmEnableSIMD:true,wasmEnableExceptionHandling:true},...l}),i.getDotnetRuntime?tl=i.getDotnetRuntime.__list:(i.getDotnetRuntime=e=>i.getDotnetRuntime.__list.getRuntime(e),i.getDotnetRuntime.__list=tl=new rl),it$1}class rl{constructor(){this.list={};}registerRuntime(e){return void 0===e.runtimeId&&(e.runtimeId=Object.keys(this.list).length),this.list[e.runtimeId]=yr(e),lt$1.config.runtimeId=e.runtimeId,e.runtimeId}getRuntime(e){const t=this.list[e];return t?t.deref():void 0}}

    var dotnet_runtime_zbexyp8zrs_js = /*#__PURE__*/Object.freeze({
        __proto__: null,
        configureEmscriptenStartup: Yc,
        configureRuntimeStartup: Qc,
        configureWorkerStartup: el,
        initializeExports: nl,
        initializeReplacements: Ao,
        passEmscriptenInternals: dt$1,
        get runtimeList () { return tl; },
        setRuntimeGlobals: ft$1
    });

    var createDotnetRuntime = (() => {
      var _scriptDir = new URL("_framework/dotnet.native.smyhto3fbr.js" + String(__spawnjsBundleUrl).replace(/^[^?#]*/, ''), __spawnjsBundleUrl).href;
      
      return (
    async function(moduleArg = {}) {

    // include: shell.js
    // The Module object: Our interface to the outside world. We import
    // and export values on it. There are various ways Module can be used:
    // 1. Not defined. We create it here
    // 2. A function parameter, function(Module) { ..generated code.. }
    // 3. pre-run appended it, var Module = {}; ..generated code..
    // 4. External script tag defines var Module.
    // We need to check if Module already exists (e.g. case 3 above).
    // Substitution will be replaced with actual code on later stage of the build,
    // this way Closure Compiler will not mangle it (e.g. case 4. above).
    // Note that if you want to run closure, and also to use Module
    // after the generated code, you will need to define   var Module = {};
    // before the code. Then that object will be used in the code, and you
    // can continue to use Module afterwards as well.
    var Module = moduleArg;

    // Set up the promise that indicates the Module is initialized
    var readyPromiseResolve, readyPromiseReject;
    Module['ready'] = new Promise((resolve, reject) => {
      readyPromiseResolve = resolve;
      readyPromiseReject = reject;
    });

    // --pre-jses are emitted after the Module integration code, so that they can
    // refer to Module (if they choose; they can also define Module)
    // include: /usr/share/dotnet/packs/Microsoft.NETCore.App.Runtime.Mono.browser-wasm/10.0.11/runtimes/browser-wasm/native/src/es6/dotnet.es6.pre.js
    if (_nativeModuleLoaded) throw new Error("Native module already loaded");
    _nativeModuleLoaded = true;
    createDotnetRuntime = Module = moduleArg(Module);
    // end include: /usr/share/dotnet/packs/Microsoft.NETCore.App.Runtime.Mono.browser-wasm/10.0.11/runtimes/browser-wasm/native/src/es6/dotnet.es6.pre.js


    // Sometimes an existing Module object exists with properties
    // meant to overwrite the default module functionality. Here
    // we collect those properties and reapply _after_ we configure
    // the current environment's defaults to avoid having to be so
    // defensive during initialization.
    var moduleOverrides = Object.assign({}, Module);
    var thisProgram = './this.program';
    var quit_ = (status, toThrow) => {
      throw toThrow;
    };

    // Determine the runtime environment we are in. You can customize this by
    // setting the ENVIRONMENT setting at compile time (see settings.js).

    // Attempt to auto-detect the environment
    var ENVIRONMENT_IS_WEB = typeof window == 'object';
    var ENVIRONMENT_IS_WORKER = typeof importScripts == 'function';
    // N.b. Electron.js environment is simultaneously a NODE-environment, but
    // also a web environment.
    var ENVIRONMENT_IS_NODE = typeof process == 'object' && typeof process.versions == 'object' && typeof process.versions.node == 'string';
    var ENVIRONMENT_IS_SHELL = !ENVIRONMENT_IS_WEB && !ENVIRONMENT_IS_NODE && !ENVIRONMENT_IS_WORKER;

    // `/` should be present at the end if `scriptDirectory` is not empty
    var scriptDirectory = '';
    function locateFile(path) {
      if (Module['locateFile']) {
        return Module['locateFile'](path, scriptDirectory);
      }
      return scriptDirectory + path;
    }

    // Hooks that are implemented differently in different runtime environments.
    var read_,
        readAsync,
        readBinary;

    if (ENVIRONMENT_IS_NODE) {

      // `require()` is no-op in an ESM module, use `createRequire()` to construct
      // the require()` function.  This is only necessary for multi-environment
      // builds, `-sENVIRONMENT=node` emits a static import declaration instead.
      // TODO: Swap all `require()`'s with `import()`'s?
      const { createRequire } = await import('module');
      /** @suppress{duplicate} */
      var require = createRequire(new URL("_framework/dotnet.native.smyhto3fbr.js" + String(__spawnjsBundleUrl).replace(/^[^?#]*/, ''), __spawnjsBundleUrl).href);
      // These modules will usually be used on Node.js. Load them eagerly to avoid
      // the complexity of lazy-loading.
      var fs = require('fs');
      var nodePath = require('path');

      if (ENVIRONMENT_IS_WORKER) {
        scriptDirectory = nodePath.dirname(scriptDirectory) + '/';
      } else {
        // EXPORT_ES6 + ENVIRONMENT_IS_NODE always requires use of import.meta.url,
        // since there's no way getting the current absolute path of the module when
        // support for that is not available.
        scriptDirectory = require('url').fileURLToPath(new URL('./', new URL("_framework/dotnet.native.smyhto3fbr.js" + String(__spawnjsBundleUrl).replace(/^[^?#]*/, ''), __spawnjsBundleUrl).href)); // includes trailing slash
      }

    // include: node_shell_read.js
    read_ = (filename, binary) => {
      // We need to re-wrap `file://` strings to URLs. Normalizing isn't
      // necessary in that case, the path should already be absolute.
      filename = isFileURI(filename) ? new URL(filename) : nodePath.normalize(filename);
      return fs.readFileSync(filename, binary ? undefined : 'utf8');
    };

    readBinary = (filename) => {
      var ret = read_(filename, true);
      if (!ret.buffer) {
        ret = new Uint8Array(ret);
      }
      return ret;
    };

    readAsync = (filename, onload, onerror, binary = true) => {
      // See the comment in the `read_` function.
      filename = isFileURI(filename) ? new URL(filename) : nodePath.normalize(filename);
      fs.readFile(filename, binary ? undefined : 'utf8', (err, data) => {
        if (err) onerror(err);
        else onload(binary ? data.buffer : data);
      });
    };
    // end include: node_shell_read.js
      if (!Module['thisProgram'] && process.argv.length > 1) {
        thisProgram = process.argv[1].replace(/\\/g, '/');
      }

      process.argv.slice(2);

      // MODULARIZE will export the module in the proper place outside, we don't need to export here

      quit_ = (status, toThrow) => {
        process.exitCode = status;
        throw toThrow;
      };

    } else
    if (ENVIRONMENT_IS_SHELL) {

      if (typeof read != 'undefined') {
        read_ = read;
      }

      readBinary = (f) => {
        if (typeof readbuffer == 'function') {
          return new Uint8Array(readbuffer(f));
        }
        let data = read(f, 'binary');
        assert(typeof data == 'object');
        return data;
      };

      readAsync = (f, onload, onerror) => {
        setTimeout(() => onload(readBinary(f)));
      };

      if (typeof clearTimeout == 'undefined') {
        globalThis.clearTimeout = (id) => {};
      }

      if (typeof setTimeout == 'undefined') {
        // spidermonkey lacks setTimeout but we use it above in readAsync.
        globalThis.setTimeout = (f) => (typeof f == 'function') ? f() : abort();
      }

      if (typeof scriptArgs != 'undefined') {
        scriptArgs;
      }

      if (typeof quit == 'function') {
        quit_ = (status, toThrow) => {
          // Unlike node which has process.exitCode, d8 has no such mechanism. So we
          // have no way to set the exit code and then let the program exit with
          // that code when it naturally stops running (say, when all setTimeouts
          // have completed). For that reason, we must call `quit` - the only way to
          // set the exit code - but quit also halts immediately.  To increase
          // consistency with node (and the web) we schedule the actual quit call
          // using a setTimeout to give the current stack and any exception handlers
          // a chance to run.  This enables features such as addOnPostRun (which
          // expected to be able to run code after main returns).
          setTimeout(() => {
            if (!(toThrow instanceof ExitStatus)) {
              let toLog = toThrow;
              if (toThrow && typeof toThrow == 'object' && toThrow.stack) {
                toLog = [toThrow, toThrow.stack];
              }
              err(`exiting due to exception: ${toLog}`);
            }
            quit(status);
          });
          throw toThrow;
        };
      }

      if (typeof print != 'undefined') {
        // Prefer to use print/printErr where they exist, as they usually work better.
        if (typeof console == 'undefined') console = /** @type{!Console} */({});
        console.log = /** @type{!function(this:Console, ...*): undefined} */ (print);
        console.warn = console.error = /** @type{!function(this:Console, ...*): undefined} */ (typeof printErr != 'undefined' ? printErr : print);
      }

    } else

    // Note that this includes Node.js workers when relevant (pthreads is enabled).
    // Node.js workers are detected as a combination of ENVIRONMENT_IS_WORKER and
    // ENVIRONMENT_IS_NODE.
    if (ENVIRONMENT_IS_WEB || ENVIRONMENT_IS_WORKER) {
      if (ENVIRONMENT_IS_WORKER) { // Check worker, not web, since window could be polyfilled
        scriptDirectory = self.location.href;
      } else if (typeof document != 'undefined' && document.currentScript) { // web
        scriptDirectory = document.currentScript.src;
      }
      // When MODULARIZE, this JS may be executed later, after document.currentScript
      // is gone, so we saved it, and we use it here instead of any other info.
      if (_scriptDir) {
        scriptDirectory = _scriptDir;
      }
      // blob urls look like blob:http://site.com/etc/etc and we cannot infer anything from them.
      // otherwise, slice off the final part of the url to find the script directory.
      // if scriptDirectory does not contain a slash, lastIndexOf will return -1,
      // and scriptDirectory will correctly be replaced with an empty string.
      // If scriptDirectory contains a query (starting with ?) or a fragment (starting with #),
      // they are removed because they could contain a slash.
      if (scriptDirectory.startsWith('blob:')) {
        scriptDirectory = '';
      } else {
        scriptDirectory = scriptDirectory.substr(0, scriptDirectory.replace(/[?#].*/, '').lastIndexOf('/')+1);
      }

      // Differentiate the Web Worker from the Node Worker case, as reading must
      // be done differently.
      {
    // include: web_or_worker_shell_read.js
    read_ = (url) => {
        var xhr = new XMLHttpRequest();
        xhr.open('GET', url, false);
        xhr.send(null);
        return xhr.responseText;
      };

      if (ENVIRONMENT_IS_WORKER) {
        readBinary = (url) => {
          var xhr = new XMLHttpRequest();
          xhr.open('GET', url, false);
          xhr.responseType = 'arraybuffer';
          xhr.send(null);
          return new Uint8Array(/** @type{!ArrayBuffer} */(xhr.response));
        };
      }

      readAsync = (url, onload, onerror) => {
        var xhr = new XMLHttpRequest();
        xhr.open('GET', url, true);
        xhr.responseType = 'arraybuffer';
        xhr.onload = () => {
          if (xhr.status == 200 || (xhr.status == 0 && xhr.response)) { // file URLs can return 0
            onload(xhr.response);
            return;
          }
          onerror();
        };
        xhr.onerror = onerror;
        xhr.send(null);
      };

    // end include: web_or_worker_shell_read.js
      }
    } else
    ;

    var out = Module['print'] || console.log.bind(console);
    var err = Module['printErr'] || console.error.bind(console);

    // Merge back in the overrides
    Object.assign(Module, moduleOverrides);
    // Free the object hierarchy contained in the overrides, this lets the GC
    // reclaim data used.
    moduleOverrides = null;

    // Emit code to handle expected values on the Module object. This applies Module.x
    // to the proper local x. This has two benefits: first, we only emit it if it is
    // expected to arrive, and second, by using a local everywhere else that can be
    // minified.

    if (Module['arguments']) Module['arguments'];

    if (Module['thisProgram']) thisProgram = Module['thisProgram'];

    if (Module['quit']) quit_ = Module['quit'];

    // perform assertions in shell.js after we set up out() and err(), as otherwise if an assertion fails it cannot print the message
    // end include: shell.js

    // include: preamble.js
    // === Preamble library stuff ===

    // Documentation for the public APIs defined in this file must be updated in:
    //    site/source/docs/api_reference/preamble.js.rst
    // A prebuilt local version of the documentation is available at:
    //    site/build/text/docs/api_reference/preamble.js.txt
    // You can also build docs locally as HTML or other formats in site/
    // An online HTML version (which may be of a different version of Emscripten)
    //    is up at http://kripken.github.io/emscripten-site/docs/api_reference/preamble.js.html

    var wasmBinary; 
    if (Module['wasmBinary']) wasmBinary = Module['wasmBinary'];

    // include: base64Utils.js
    // include: polyfill/atob.js
    // Copied from https://github.com/strophe/strophejs/blob/e06d027/src/polyfills.js#L149

    // This code was written by Tyler Akins and has been placed in the
    // public domain.  It would be nice if you left this header intact.
    // Base64 code from Tyler Akins -- http://rumkin.com

    if (typeof atob == 'undefined') {
      if (typeof global != 'undefined' && typeof globalThis == 'undefined') {
        globalThis = global;
      }

      /**
       * Decodes a base64 string.
       * @param {string} input The string to decode.
       */
      globalThis.atob = function(input) {
        var keyStr = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=';

        var output = '';
        var chr1, chr2, chr3;
        var enc1, enc2, enc3, enc4;
        var i = 0;
        // remove all characters that are not A-Z, a-z, 0-9, +, /, or =
        input = input.replace(/[^A-Za-z0-9\+\/\=]/g, '');
        do {
          enc1 = keyStr.indexOf(input.charAt(i++));
          enc2 = keyStr.indexOf(input.charAt(i++));
          enc3 = keyStr.indexOf(input.charAt(i++));
          enc4 = keyStr.indexOf(input.charAt(i++));

          chr1 = (enc1 << 2) | (enc2 >> 4);
          chr2 = ((enc2 & 15) << 4) | (enc3 >> 2);
          chr3 = ((enc3 & 3) << 6) | enc4;

          output = output + String.fromCharCode(chr1);

          if (enc3 !== 64) {
            output = output + String.fromCharCode(chr2);
          }
          if (enc4 !== 64) {
            output = output + String.fromCharCode(chr3);
          }
        } while (i < input.length);
        return output;
      };
    }
    // end include: base64Utils.js
    // Wasm globals

    var wasmMemory;

    //========================================
    // Runtime essentials
    //========================================

    // whether we are quitting the application. no code should run after this.
    // set in exit() and abort()
    var ABORT = false;

    // set by exit() and abort().  Passed to 'onExit' handler.
    // NOTE: This is also used as the process return code code in shell environments
    // but only when noExitRuntime is false.
    var EXITSTATUS;

    // In STRICT mode, we only define assert() when ASSERTIONS is set.  i.e. we
    // don't define it at all in release modes.  This matches the behaviour of
    // MINIMAL_RUNTIME.
    // TODO(sbc): Make this the default even without STRICT enabled.
    /** @type {function(*, string=)} */
    function assert(condition, text) {
      if (!condition) {
        // This build was created without ASSERTIONS defined.  `assert()` should not
        // ever be called in this configuration but in case there are callers in
        // the wild leave this simple abort() implementation here for now.
        abort(text);
      }
    }

    // Memory management

    var /** @type {!Int8Array} */
      HEAP8,
    /** @type {!Uint8Array} */
      HEAPU8,
    /** @type {!Int16Array} */
      HEAP16,
    /** @type {!Uint16Array} */
      HEAPU16,
    /** @type {!Int32Array} */
      HEAP32,
    /** @type {!Uint32Array} */
      HEAPU32,
    /** @type {!Float32Array} */
      HEAPF32,
    /* BigInt64Array type is not correctly defined in closure
    /** not-@type {!BigInt64Array} */
      HEAP64,
    /** @type {!Float64Array} */
      HEAPF64;

    // include: runtime_shared.js
    function updateMemoryViews() {
      var b = wasmMemory.buffer;
      Module['HEAP8'] = HEAP8 = new Int8Array(b);
      Module['HEAP16'] = HEAP16 = new Int16Array(b);
      Module['HEAPU8'] = HEAPU8 = new Uint8Array(b);
      Module['HEAPU16'] = HEAPU16 = new Uint16Array(b);
      Module['HEAP32'] = HEAP32 = new Int32Array(b);
      Module['HEAPU32'] = HEAPU32 = new Uint32Array(b);
      Module['HEAPF32'] = HEAPF32 = new Float32Array(b);
      Module['HEAPF64'] = HEAPF64 = new Float64Array(b);
      Module['HEAP64'] = HEAP64 = new BigInt64Array(b);
      Module['HEAPU64'] = new BigUint64Array(b);
    }
    // end include: runtime_shared.js
    // include: runtime_stack_check.js
    // end include: runtime_stack_check.js
    // include: runtime_assertions.js
    // end include: runtime_assertions.js
    var __ATPRERUN__  = []; // functions called before the runtime is initialized
    var __ATINIT__    = []; // functions called during startup
    var __ATEXIT__    = []; // functions called during shutdown
    var __ATPOSTRUN__ = []; // functions called after the main() is called

    var runtimeInitialized = false;

    var runtimeExited = false;

    function preRun() {
      if (Module['preRun']) {
        if (typeof Module['preRun'] == 'function') Module['preRun'] = [Module['preRun']];
        while (Module['preRun'].length) {
          addOnPreRun(Module['preRun'].shift());
        }
      }
      callRuntimeCallbacks(__ATPRERUN__);
    }

    function initRuntime() {
      runtimeInitialized = true;

      
    if (!Module['noFSInit'] && !FS.init.initialized)
      FS.init();
    FS.ignorePermissions = false;
    SOCKFS.root = FS.mount(SOCKFS, {}, null);
      callRuntimeCallbacks(__ATINIT__);
    }

    function exitRuntime() {
      ___funcs_on_exit(); // Native atexit() functions
      callRuntimeCallbacks(__ATEXIT__);
      FS.quit();
      runtimeExited = true;
    }

    function postRun() {

      if (Module['postRun']) {
        if (typeof Module['postRun'] == 'function') Module['postRun'] = [Module['postRun']];
        while (Module['postRun'].length) {
          addOnPostRun(Module['postRun'].shift());
        }
      }

      callRuntimeCallbacks(__ATPOSTRUN__);
    }

    function addOnPreRun(cb) {
      __ATPRERUN__.unshift(cb);
    }

    function addOnInit(cb) {
      __ATINIT__.unshift(cb);
    }

    function addOnPostRun(cb) {
      __ATPOSTRUN__.unshift(cb);
    }

    // include: runtime_math.js
    // https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Math/imul

    // https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Math/fround

    // https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Math/clz32

    // https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Math/trunc

    // end include: runtime_math.js
    // A counter of dependencies for calling run(). If we need to
    // do asynchronous work before running, increment this and
    // decrement it. Incrementing must happen in a place like
    // Module.preRun (used by emcc to add file preloading).
    // Note that you can add dependencies in preRun, even though
    // it happens right before run - run will be postponed until
    // the dependencies are met.
    var runDependencies = 0;
    var dependenciesFulfilled = null; // overridden to take different actions when all run dependencies are fulfilled

    function getUniqueRunDependency(id) {
      return id;
    }

    function addRunDependency(id) {
      runDependencies++;

      Module['monitorRunDependencies']?.(runDependencies);

    }

    function removeRunDependency(id) {
      runDependencies--;

      Module['monitorRunDependencies']?.(runDependencies);

      if (runDependencies == 0) {
        if (dependenciesFulfilled) {
          var callback = dependenciesFulfilled;
          dependenciesFulfilled = null;
          callback(); // can add another dependenciesFulfilled
        }
      }
    }

    /** @param {string|number=} what */
    function abort(what) {
      Module['onAbort']?.(what);

      what = 'Aborted(' + what + ')';
      // TODO(sbc): Should we remove printing and leave it up to whoever
      // catches the exception?
      err(what);

      ABORT = true;
      EXITSTATUS = 1;

      what += '. Build with -sASSERTIONS for more info.';

      // Use a wasm runtime error, because a JS error might be seen as a foreign
      // exception, which means we'd run destructors on it. We need the error to
      // simply make the program stop.
      // FIXME This approach does not work in Wasm EH because it currently does not assume
      // all RuntimeErrors are from traps; it decides whether a RuntimeError is from
      // a trap or not based on a hidden field within the object. So at the moment
      // we don't have a way of throwing a wasm trap from JS. TODO Make a JS API that
      // allows this in the wasm spec.

      // Suppress closure compiler warning here. Closure compiler's builtin extern
      // definition for WebAssembly.RuntimeError claims it takes no arguments even
      // though it can.
      // TODO(https://github.com/google/closure-compiler/pull/3913): Remove if/when upstream closure gets fixed.
      // See above, in the meantime, we resort to wasm code for trapping.
      //
      // In case abort() is called before the module is initialized, wasmExports
      // and its exported '__trap' function is not available, in which case we throw
      // a RuntimeError.
      //
      // We trap instead of throwing RuntimeError to prevent infinite-looping in
      // Wasm EH code (because RuntimeError is considered as a foreign exception and
      // caught by 'catch_all'), but in case throwing RuntimeError is fine because
      // the module has not even been instantiated, even less running.
      if (runtimeInitialized) {
        ___trap();
      }
      /** @suppress {checkTypes} */
      var e = new WebAssembly.RuntimeError(what);

      readyPromiseReject(e);
      // Throw the error whether or not MODULARIZE is set because abort is used
      // in code paths apart from instantiation where an exception is expected
      // to be thrown when abort is called.
      throw e;
    }

    // include: memoryprofiler.js
    // end include: memoryprofiler.js
    // include: URIUtils.js
    // Prefix of data URIs emitted by SINGLE_FILE and related options.
    var dataURIPrefix = 'data:application/octet-stream;base64,';

    /**
     * Indicates whether filename is a base64 data URI.
     * @noinline
     */
    var isDataURI = (filename) => filename.startsWith(dataURIPrefix);

    /**
     * Indicates whether filename is delivered via file protocol (as opposed to http/https)
     * @noinline
     */
    var isFileURI = (filename) => filename.startsWith('file://');
    // end include: URIUtils.js
    // include: runtime_exceptions.js
    // end include: runtime_exceptions.js
    var wasmBinaryFile;
    if (Module['locateFile']) {
      wasmBinaryFile = 'dotnet.native.wasm';
      if (!isDataURI(wasmBinaryFile)) {
        wasmBinaryFile = locateFile(wasmBinaryFile);
      }
    } else {
      if (ENVIRONMENT_IS_SHELL)
        wasmBinaryFile = 'dotnet.native.wasm';
      else
      // Use bundler-friendly `new URL(..., import.meta.url)` pattern; works in browsers too.
      wasmBinaryFile = new URL('dotnet.native.wasm', new URL("_framework/dotnet.native.smyhto3fbr.js" + String(__spawnjsBundleUrl).replace(/^[^?#]*/, ''), __spawnjsBundleUrl).href).href;
    }

    function getBinarySync(file) {
      if (file == wasmBinaryFile && wasmBinary) {
        return new Uint8Array(wasmBinary);
      }
      if (readBinary) {
        return readBinary(file);
      }
      throw 'both async and sync fetching of the wasm failed';
    }

    function getBinaryPromise(binaryFile) {
      // If we don't have the binary yet, try to load it asynchronously.
      // Fetch has some additional restrictions over XHR, like it can't be used on a file:// url.
      // See https://github.com/github/fetch/pull/92#issuecomment-140665932
      // Cordova or Electron apps are typically loaded from a file:// url.
      // So use fetch if it is available and the url is not a file, otherwise fall back to XHR.
      if (!wasmBinary
          && (ENVIRONMENT_IS_WEB || ENVIRONMENT_IS_WORKER)) {
        if (typeof fetch == 'function'
          && !isFileURI(binaryFile)
        ) {
          return fetch(binaryFile, { credentials: 'same-origin' }).then((response) => {
            if (!response['ok']) {
              throw `failed to load wasm binary file at '${binaryFile}'`;
            }
            return response['arrayBuffer']();
          }).catch(() => getBinarySync(binaryFile));
        }
        else if (readAsync) {
          // fetch is not available or url is file => try XHR (readAsync uses XHR internally)
          return new Promise((resolve, reject) => {
            readAsync(binaryFile, (response) => resolve(new Uint8Array(/** @type{!ArrayBuffer} */(response))), reject);
          });
        }
      }

      // Otherwise, getBinarySync should be able to get it synchronously
      return Promise.resolve().then(() => getBinarySync(binaryFile));
    }

    function instantiateArrayBuffer(binaryFile, imports, receiver) {
      return getBinaryPromise(binaryFile).then((binary) => {
        return WebAssembly.instantiate(binary, imports);
      }).then(receiver, (reason) => {
        err(`failed to asynchronously prepare wasm: ${reason}`);

        abort(reason);
      });
    }

    function instantiateAsync(binary, binaryFile, imports, callback) {
      if (!binary &&
          typeof WebAssembly.instantiateStreaming == 'function' &&
          !isDataURI(binaryFile) &&
          // Don't use streaming for file:// delivered objects in a webview, fetch them synchronously.
          !isFileURI(binaryFile) &&
          // Avoid instantiateStreaming() on Node.js environment for now, as while
          // Node.js v18.1.0 implements it, it does not have a full fetch()
          // implementation yet.
          //
          // Reference:
          //   https://github.com/emscripten-core/emscripten/pull/16917
          !ENVIRONMENT_IS_NODE &&
          typeof fetch == 'function') {
        return fetch(binaryFile, { credentials: 'same-origin' }).then((response) => {
          // Suppress closure warning here since the upstream definition for
          // instantiateStreaming only allows Promise<Repsponse> rather than
          // an actual Response.
          // TODO(https://github.com/google/closure-compiler/pull/3913): Remove if/when upstream closure is fixed.
          /** @suppress {checkTypes} */
          var result = WebAssembly.instantiateStreaming(response, imports);

          return result.then(
            callback,
            function(reason) {
              // We expect the most common failure cause to be a bad MIME type for the binary,
              // in which case falling back to ArrayBuffer instantiation should work.
              err(`wasm streaming compile failed: ${reason}`);
              err('falling back to ArrayBuffer instantiation');
              return instantiateArrayBuffer(binaryFile, imports, callback);
            });
        });
      }
      return instantiateArrayBuffer(binaryFile, imports, callback);
    }

    // Create the wasm instance.
    // Receives the wasm imports, returns the exports.
    function createWasm() {
      // prepare imports
      var info = {
        'env': wasmImports,
        'wasi_snapshot_preview1': wasmImports,
      };
      // Load the wasm module and create an instance of using native support in the JS engine.
      // handle a generated wasm instance, receiving its exports and
      // performing other necessary setup
      /** @param {WebAssembly.Module=} module*/
      function receiveInstance(instance, module) {
        wasmExports = instance.exports;

        Module['wasmExports'] = wasmExports;

        wasmMemory = wasmExports['memory'];
        
        updateMemoryViews();

        wasmTable = wasmExports['__indirect_function_table'];
        

        addOnInit(wasmExports['__wasm_call_ctors']);

        removeRunDependency();
        return wasmExports;
      }
      // wait for the pthread pool (if any)
      addRunDependency();

      // Prefer streaming instantiation if available.
      function receiveInstantiationResult(result) {
        // 'result' is a ResultObject object which has both the module and instance.
        // receiveInstance() will swap in the exports (to Module.asm) so they can be called
        // TODO: Due to Closure regression https://github.com/google/closure-compiler/issues/3193, the above line no longer optimizes out down to the following line.
        // When the regression is fixed, can restore the above PTHREADS-enabled path.
        receiveInstance(result['instance']);
      }

      // User shell pages can write their own Module.instantiateWasm = function(imports, successCallback) callback
      // to manually instantiate the Wasm module themselves. This allows pages to
      // run the instantiation parallel to any other async startup actions they are
      // performing.
      // Also pthreads and wasm workers initialize the wasm instance through this
      // path.
      if (Module['instantiateWasm']) {

        try {
          return Module['instantiateWasm'](info, receiveInstance);
        } catch(e) {
          err(`Module.instantiateWasm callback failed with error: ${e}`);
            // If instantiation fails, reject the module ready promise.
            readyPromiseReject(e);
        }
      }

      // If instantiation fails, reject the module ready promise.
      instantiateAsync(wasmBinary, wasmBinaryFile, info, receiveInstantiationResult).catch(readyPromiseReject);
      return {}; // no exports yet; we'll fill them in later
    }

    // include: runtime_debug.js
    // end include: runtime_debug.js
    // === Body ===
    // end include: preamble.js


      /** @constructor */
      function ExitStatus(status) {
          this.name = 'ExitStatus';
          this.message = `Program terminated with exit(${status})`;
          this.status = status;
        }

      var callRuntimeCallbacks = (callbacks) => {
          while (callbacks.length > 0) {
            // Pass the module as the first argument.
            callbacks.shift()(Module);
          }
        };

      
        /**
         * @param {number} ptr
         * @param {string} type
         */
      function getValue(ptr, type = 'i8') {
        if (type.endsWith('*')) type = '*';
        switch (type) {
          case 'i1': return HEAP8[ptr];
          case 'i8': return HEAP8[ptr];
          case 'i16': return HEAP16[((ptr)>>1)];
          case 'i32': return HEAP32[((ptr)>>2)];
          case 'i64': return HEAP64[((ptr)>>3)];
          case 'float': return HEAPF32[((ptr)>>2)];
          case 'double': return HEAPF64[((ptr)>>3)];
          case '*': return HEAPU32[((ptr)>>2)];
          default: abort(`invalid type for getValue: ${type}`);
        }
      }

      var noExitRuntime = Module['noExitRuntime'] || false;

      
        /**
         * @param {number} ptr
         * @param {number} value
         * @param {string} type
         */
      function setValue(ptr, value, type = 'i8') {
        if (type.endsWith('*')) type = '*';
        switch (type) {
          case 'i1': HEAP8[ptr] = value; break;
          case 'i8': HEAP8[ptr] = value; break;
          case 'i16': HEAP16[((ptr)>>1)] = value; break;
          case 'i32': HEAP32[((ptr)>>2)] = value; break;
          case 'i64': HEAP64[((ptr)>>3)] = BigInt(value); break;
          case 'float': HEAPF32[((ptr)>>2)] = value; break;
          case 'double': HEAPF64[((ptr)>>3)] = value; break;
          case '*': HEAPU32[((ptr)>>2)] = value; break;
          default: abort(`invalid type for setValue: ${type}`);
        }
      }

      var UTF8Decoder = typeof TextDecoder != 'undefined' ? new TextDecoder('utf8') : undefined;
      
        /**
         * Given a pointer 'idx' to a null-terminated UTF8-encoded string in the given
         * array that contains uint8 values, returns a copy of that string as a
         * Javascript String object.
         * heapOrArray is either a regular array, or a JavaScript typed array view.
         * @param {number} idx
         * @param {number=} maxBytesToRead
         * @return {string}
         */
      var UTF8ArrayToString = (heapOrArray, idx, maxBytesToRead) => {
          var endIdx = idx + maxBytesToRead;
          var endPtr = idx;
          // TextDecoder needs to know the byte length in advance, it doesn't stop on
          // null terminator by itself.  Also, use the length info to avoid running tiny
          // strings through TextDecoder, since .subarray() allocates garbage.
          // (As a tiny code save trick, compare endPtr against endIdx using a negation,
          // so that undefined means Infinity)
          while (heapOrArray[endPtr] && !(endPtr >= endIdx)) ++endPtr;
      
          if (endPtr - idx > 16 && heapOrArray.buffer && UTF8Decoder) {
            return UTF8Decoder.decode(heapOrArray.subarray(idx, endPtr));
          }
          var str = '';
          // If building with TextDecoder, we have already computed the string length
          // above, so test loop end condition against that
          while (idx < endPtr) {
            // For UTF8 byte structure, see:
            // http://en.wikipedia.org/wiki/UTF-8#Description
            // https://www.ietf.org/rfc/rfc2279.txt
            // https://tools.ietf.org/html/rfc3629
            var u0 = heapOrArray[idx++];
            if (!(u0 & 0x80)) { str += String.fromCharCode(u0); continue; }
            var u1 = heapOrArray[idx++] & 63;
            if ((u0 & 0xE0) == 0xC0) { str += String.fromCharCode(((u0 & 31) << 6) | u1); continue; }
            var u2 = heapOrArray[idx++] & 63;
            if ((u0 & 0xF0) == 0xE0) {
              u0 = ((u0 & 15) << 12) | (u1 << 6) | u2;
            } else {
              u0 = ((u0 & 7) << 18) | (u1 << 12) | (u2 << 6) | (heapOrArray[idx++] & 63);
            }
      
            if (u0 < 0x10000) {
              str += String.fromCharCode(u0);
            } else {
              var ch = u0 - 0x10000;
              str += String.fromCharCode(0xD800 | (ch >> 10), 0xDC00 | (ch & 0x3FF));
            }
          }
          return str;
        };
      
        /**
         * Given a pointer 'ptr' to a null-terminated UTF8-encoded string in the
         * emscripten HEAP, returns a copy of that string as a Javascript String object.
         *
         * @param {number} ptr
         * @param {number=} maxBytesToRead - An optional length that specifies the
         *   maximum number of bytes to read. You can omit this parameter to scan the
         *   string until the first 0 byte. If maxBytesToRead is passed, and the string
         *   at [ptr, ptr+maxBytesToReadr[ contains a null byte in the middle, then the
         *   string will cut short at that byte index (i.e. maxBytesToRead will not
         *   produce a string of exact length [ptr, ptr+maxBytesToRead[) N.B. mixing
         *   frequent uses of UTF8ToString() with and without maxBytesToRead may throw
         *   JS JIT optimizations off, so it is worth to consider consistently using one
         * @return {string}
         */
      var UTF8ToString = (ptr, maxBytesToRead) => {
          return ptr ? UTF8ArrayToString(HEAPU8, ptr, maxBytesToRead) : '';
        };
      var ___assert_fail = (condition, filename, line, func) => {
          abort(`Assertion failed: ${UTF8ToString(condition)}, at: ` + [filename ? UTF8ToString(filename) : 'unknown filename', line, func ? UTF8ToString(func) : 'unknown function']);
        };

      var PATH = {
      isAbs:(path) => path.charAt(0) === '/',
      splitPath:(filename) => {
            var splitPathRe = /^(\/?|)([\s\S]*?)((?:\.{1,2}|[^\/]+?|)(\.[^.\/]*|))(?:[\/]*)$/;
            return splitPathRe.exec(filename).slice(1);
          },
      normalizeArray:(parts, allowAboveRoot) => {
            // if the path tries to go above the root, `up` ends up > 0
            var up = 0;
            for (var i = parts.length - 1; i >= 0; i--) {
              var last = parts[i];
              if (last === '.') {
                parts.splice(i, 1);
              } else if (last === '..') {
                parts.splice(i, 1);
                up++;
              } else if (up) {
                parts.splice(i, 1);
                up--;
              }
            }
            // if the path is allowed to go above the root, restore leading ..s
            if (allowAboveRoot) {
              for (; up; up--) {
                parts.unshift('..');
              }
            }
            return parts;
          },
      normalize:(path) => {
            var isAbsolute = PATH.isAbs(path),
                trailingSlash = path.substr(-1) === '/';
            // Normalize the path
            path = PATH.normalizeArray(path.split('/').filter((p) => !!p), !isAbsolute).join('/');
            if (!path && !isAbsolute) {
              path = '.';
            }
            if (path && trailingSlash) {
              path += '/';
            }
            return (isAbsolute ? '/' : '') + path;
          },
      dirname:(path) => {
            var result = PATH.splitPath(path),
                root = result[0],
                dir = result[1];
            if (!root && !dir) {
              // No dirname whatsoever
              return '.';
            }
            if (dir) {
              // It has a dirname, strip trailing slash
              dir = dir.substr(0, dir.length - 1);
            }
            return root + dir;
          },
      basename:(path) => {
            // EMSCRIPTEN return '/'' for '/', not an empty string
            if (path === '/') return '/';
            path = PATH.normalize(path);
            path = path.replace(/\/$/, "");
            var lastSlash = path.lastIndexOf('/');
            if (lastSlash === -1) return path;
            return path.substr(lastSlash+1);
          },
      join:(...paths) => PATH.normalize(paths.join('/')),
      join2:(l, r) => PATH.normalize(l + '/' + r),
      };
      
      var initRandomFill = () => {
          if (typeof crypto == 'object' && typeof crypto['getRandomValues'] == 'function') {
            // for modern web browsers
            return (view) => crypto.getRandomValues(view);
          } else
          if (ENVIRONMENT_IS_NODE) {
            // for nodejs with or without crypto support included
            try {
              var crypto_module = require('crypto');
              var randomFillSync = crypto_module['randomFillSync'];
              if (randomFillSync) {
                // nodejs with LTS crypto support
                return (view) => crypto_module['randomFillSync'](view);
              }
              // very old nodejs with the original crypto API
              var randomBytes = crypto_module['randomBytes'];
              return (view) => (
                view.set(randomBytes(view.byteLength)),
                // Return the original view to match modern native implementations.
                view
              );
            } catch (e) {
              // nodejs doesn't have crypto support
            }
          }
          // we couldn't find a proper implementation, as Math.random() is not suitable for /dev/random, see emscripten-core/emscripten/pull/7096
          abort('initRandomDevice');
        };
      var randomFill = (view) => {
          // Lazily init on the first invocation.
          return (randomFill = initRandomFill())(view);
        };
      
      
      
      var PATH_FS = {
      resolve:(...args) => {
            var resolvedPath = '',
              resolvedAbsolute = false;
            for (var i = args.length - 1; i >= -1 && !resolvedAbsolute; i--) {
              var path = (i >= 0) ? args[i] : FS.cwd();
              // Skip empty and invalid entries
              if (typeof path != 'string') {
                throw new TypeError('Arguments to path.resolve must be strings');
              } else if (!path) {
                return ''; // an invalid portion invalidates the whole thing
              }
              resolvedPath = path + '/' + resolvedPath;
              resolvedAbsolute = PATH.isAbs(path);
            }
            // At this point the path should be resolved to a full absolute path, but
            // handle relative paths to be safe (might happen when process.cwd() fails)
            resolvedPath = PATH.normalizeArray(resolvedPath.split('/').filter((p) => !!p), !resolvedAbsolute).join('/');
            return ((resolvedAbsolute ? '/' : '') + resolvedPath) || '.';
          },
      relative:(from, to) => {
            from = PATH_FS.resolve(from).substr(1);
            to = PATH_FS.resolve(to).substr(1);
            function trim(arr) {
              var start = 0;
              for (; start < arr.length; start++) {
                if (arr[start] !== '') break;
              }
              var end = arr.length - 1;
              for (; end >= 0; end--) {
                if (arr[end] !== '') break;
              }
              if (start > end) return [];
              return arr.slice(start, end - start + 1);
            }
            var fromParts = trim(from.split('/'));
            var toParts = trim(to.split('/'));
            var length = Math.min(fromParts.length, toParts.length);
            var samePartsLength = length;
            for (var i = 0; i < length; i++) {
              if (fromParts[i] !== toParts[i]) {
                samePartsLength = i;
                break;
              }
            }
            var outputParts = [];
            for (var i = samePartsLength; i < fromParts.length; i++) {
              outputParts.push('..');
            }
            outputParts = outputParts.concat(toParts.slice(samePartsLength));
            return outputParts.join('/');
          },
      };
      
      
      
      var FS_stdin_getChar_buffer = [];
      
      var lengthBytesUTF8 = (str) => {
          var len = 0;
          for (var i = 0; i < str.length; ++i) {
            // Gotcha: charCodeAt returns a 16-bit word that is a UTF-16 encoded code
            // unit, not a Unicode code point of the character! So decode
            // UTF16->UTF32->UTF8.
            // See http://unicode.org/faq/utf_bom.html#utf16-3
            var c = str.charCodeAt(i); // possibly a lead surrogate
            if (c <= 0x7F) {
              len++;
            } else if (c <= 0x7FF) {
              len += 2;
            } else if (c >= 0xD800 && c <= 0xDFFF) {
              len += 4; ++i;
            } else {
              len += 3;
            }
          }
          return len;
        };
      
      var stringToUTF8Array = (str, heap, outIdx, maxBytesToWrite) => {
          // Parameter maxBytesToWrite is not optional. Negative values, 0, null,
          // undefined and false each don't write out any bytes.
          if (!(maxBytesToWrite > 0))
            return 0;
      
          var startIdx = outIdx;
          var endIdx = outIdx + maxBytesToWrite - 1; // -1 for string null terminator.
          for (var i = 0; i < str.length; ++i) {
            // Gotcha: charCodeAt returns a 16-bit word that is a UTF-16 encoded code
            // unit, not a Unicode code point of the character! So decode
            // UTF16->UTF32->UTF8.
            // See http://unicode.org/faq/utf_bom.html#utf16-3
            // For UTF8 byte structure, see http://en.wikipedia.org/wiki/UTF-8#Description
            // and https://www.ietf.org/rfc/rfc2279.txt
            // and https://tools.ietf.org/html/rfc3629
            var u = str.charCodeAt(i); // possibly a lead surrogate
            if (u >= 0xD800 && u <= 0xDFFF) {
              var u1 = str.charCodeAt(++i);
              u = 0x10000 + ((u & 0x3FF) << 10) | (u1 & 0x3FF);
            }
            if (u <= 0x7F) {
              if (outIdx >= endIdx) break;
              heap[outIdx++] = u;
            } else if (u <= 0x7FF) {
              if (outIdx + 1 >= endIdx) break;
              heap[outIdx++] = 0xC0 | (u >> 6);
              heap[outIdx++] = 0x80 | (u & 63);
            } else if (u <= 0xFFFF) {
              if (outIdx + 2 >= endIdx) break;
              heap[outIdx++] = 0xE0 | (u >> 12);
              heap[outIdx++] = 0x80 | ((u >> 6) & 63);
              heap[outIdx++] = 0x80 | (u & 63);
            } else {
              if (outIdx + 3 >= endIdx) break;
              heap[outIdx++] = 0xF0 | (u >> 18);
              heap[outIdx++] = 0x80 | ((u >> 12) & 63);
              heap[outIdx++] = 0x80 | ((u >> 6) & 63);
              heap[outIdx++] = 0x80 | (u & 63);
            }
          }
          // Null-terminate the pointer to the buffer.
          heap[outIdx] = 0;
          return outIdx - startIdx;
        };
      /** @type {function(string, boolean=, number=)} */
      function intArrayFromString(stringy, dontAddNull, length) {
        var len = lengthBytesUTF8(stringy)+1;
        var u8array = new Array(len);
        var numBytesWritten = stringToUTF8Array(stringy, u8array, 0, u8array.length);
        if (dontAddNull) u8array.length = numBytesWritten;
        return u8array;
      }
      var FS_stdin_getChar = () => {
          if (!FS_stdin_getChar_buffer.length) {
            var result = null;
            if (ENVIRONMENT_IS_NODE) {
              // we will read data by chunks of BUFSIZE
              var BUFSIZE = 256;
              var buf = Buffer.alloc(BUFSIZE);
              var bytesRead = 0;
      
              // For some reason we must suppress a closure warning here, even though
              // fd definitely exists on process.stdin, and is even the proper way to
              // get the fd of stdin,
              // https://github.com/nodejs/help/issues/2136#issuecomment-523649904
              // This started to happen after moving this logic out of library_tty.js,
              // so it is related to the surrounding code in some unclear manner.
              /** @suppress {missingProperties} */
              var fd = process.stdin.fd;
      
              try {
                bytesRead = fs.readSync(fd, buf);
              } catch(e) {
                // Cross-platform differences: on Windows, reading EOF throws an exception, but on other OSes,
                // reading EOF returns 0. Uniformize behavior by treating the EOF exception to return 0.
                if (e.toString().includes('EOF')) bytesRead = 0;
                else throw e;
              }
      
              if (bytesRead > 0) {
                result = buf.slice(0, bytesRead).toString('utf-8');
              } else {
                result = null;
              }
            } else
            if (typeof window != 'undefined' &&
              typeof window.prompt == 'function') {
              // Browser.
              result = window.prompt('Input: ');  // returns null on cancel
              if (result !== null) {
                result += '\n';
              }
            } else if (typeof readline == 'function') {
              // Command line.
              result = readline();
              if (result !== null) {
                result += '\n';
              }
            }
            if (!result) {
              return null;
            }
            FS_stdin_getChar_buffer = intArrayFromString(result, true);
          }
          return FS_stdin_getChar_buffer.shift();
        };
      var TTY = {
      ttys:[],
      init() {
            // https://github.com/emscripten-core/emscripten/pull/1555
            // if (ENVIRONMENT_IS_NODE) {
            //   // currently, FS.init does not distinguish if process.stdin is a file or TTY
            //   // device, it always assumes it's a TTY device. because of this, we're forcing
            //   // process.stdin to UTF8 encoding to at least make stdin reading compatible
            //   // with text files until FS.init can be refactored.
            //   process.stdin.setEncoding('utf8');
            // }
          },
      shutdown() {
            // https://github.com/emscripten-core/emscripten/pull/1555
            // if (ENVIRONMENT_IS_NODE) {
            //   // inolen: any idea as to why node -e 'process.stdin.read()' wouldn't exit immediately (with process.stdin being a tty)?
            //   // isaacs: because now it's reading from the stream, you've expressed interest in it, so that read() kicks off a _read() which creates a ReadReq operation
            //   // inolen: I thought read() in that case was a synchronous operation that just grabbed some amount of buffered data if it exists?
            //   // isaacs: it is. but it also triggers a _read() call, which calls readStart() on the handle
            //   // isaacs: do process.stdin.pause() and i'd think it'd probably close the pending call
            //   process.stdin.pause();
            // }
          },
      register(dev, ops) {
            TTY.ttys[dev] = { input: [], output: [], ops: ops };
            FS.registerDevice(dev, TTY.stream_ops);
          },
      stream_ops:{
      open(stream) {
              var tty = TTY.ttys[stream.node.rdev];
              if (!tty) {
                throw new FS.ErrnoError(43);
              }
              stream.tty = tty;
              stream.seekable = false;
            },
      close(stream) {
              // flush any pending line data
              stream.tty.ops.fsync(stream.tty);
            },
      fsync(stream) {
              stream.tty.ops.fsync(stream.tty);
            },
      read(stream, buffer, offset, length, pos /* ignored */) {
              if (!stream.tty || !stream.tty.ops.get_char) {
                throw new FS.ErrnoError(60);
              }
              var bytesRead = 0;
              for (var i = 0; i < length; i++) {
                var result;
                try {
                  result = stream.tty.ops.get_char(stream.tty);
                } catch (e) {
                  throw new FS.ErrnoError(29);
                }
                if (result === undefined && bytesRead === 0) {
                  throw new FS.ErrnoError(6);
                }
                if (result === null || result === undefined) break;
                bytesRead++;
                buffer[offset+i] = result;
              }
              if (bytesRead) {
                stream.node.timestamp = Date.now();
              }
              return bytesRead;
            },
      write(stream, buffer, offset, length, pos) {
              if (!stream.tty || !stream.tty.ops.put_char) {
                throw new FS.ErrnoError(60);
              }
              try {
                for (var i = 0; i < length; i++) {
                  stream.tty.ops.put_char(stream.tty, buffer[offset+i]);
                }
              } catch (e) {
                throw new FS.ErrnoError(29);
              }
              if (length) {
                stream.node.timestamp = Date.now();
              }
              return i;
            },
      },
      default_tty_ops:{
      get_char(tty) {
              return FS_stdin_getChar();
            },
      put_char(tty, val) {
              if (val === null || val === 10) {
                out(UTF8ArrayToString(tty.output, 0));
                tty.output = [];
              } else {
                if (val != 0) tty.output.push(val); // val == 0 would cut text output off in the middle.
              }
            },
      fsync(tty) {
              if (tty.output && tty.output.length > 0) {
                out(UTF8ArrayToString(tty.output, 0));
                tty.output = [];
              }
            },
      ioctl_tcgets(tty) {
              // typical setting
              return {
                c_iflag: 25856,
                c_oflag: 5,
                c_cflag: 191,
                c_lflag: 35387,
                c_cc: [
                  0x03, 0x1c, 0x7f, 0x15, 0x04, 0x00, 0x01, 0x00, 0x11, 0x13, 0x1a, 0x00,
                  0x12, 0x0f, 0x17, 0x16, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                  0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                ]
              };
            },
      ioctl_tcsets(tty, optional_actions, data) {
              // currently just ignore
              return 0;
            },
      ioctl_tiocgwinsz(tty) {
              return [24, 80];
            },
      },
      default_tty1_ops:{
      put_char(tty, val) {
              if (val === null || val === 10) {
                err(UTF8ArrayToString(tty.output, 0));
                tty.output = [];
              } else {
                if (val != 0) tty.output.push(val);
              }
            },
      fsync(tty) {
              if (tty.output && tty.output.length > 0) {
                err(UTF8ArrayToString(tty.output, 0));
                tty.output = [];
              }
            },
      },
      };
      
      
      var zeroMemory = (address, size) => {
          HEAPU8.fill(0, address, address + size);
          return address;
        };
      
      var alignMemory = (size, alignment) => {
          return Math.ceil(size / alignment) * alignment;
        };
      var mmapAlloc = (size) => {
          size = alignMemory(size, 65536);
          var ptr = _emscripten_builtin_memalign(65536, size);
          if (!ptr) return 0;
          return zeroMemory(ptr, size);
        };
      var MEMFS = {
      ops_table:null,
      mount(mount) {
            return MEMFS.createNode(null, '/', 16384 | 511 /* 0777 */, 0);
          },
      createNode(parent, name, mode, dev) {
            if (FS.isBlkdev(mode) || FS.isFIFO(mode)) {
              // no supported
              throw new FS.ErrnoError(63);
            }
            MEMFS.ops_table ||= {
              dir: {
                node: {
                  getattr: MEMFS.node_ops.getattr,
                  setattr: MEMFS.node_ops.setattr,
                  lookup: MEMFS.node_ops.lookup,
                  mknod: MEMFS.node_ops.mknod,
                  rename: MEMFS.node_ops.rename,
                  unlink: MEMFS.node_ops.unlink,
                  rmdir: MEMFS.node_ops.rmdir,
                  readdir: MEMFS.node_ops.readdir,
                  symlink: MEMFS.node_ops.symlink
                },
                stream: {
                  llseek: MEMFS.stream_ops.llseek
                }
              },
              file: {
                node: {
                  getattr: MEMFS.node_ops.getattr,
                  setattr: MEMFS.node_ops.setattr
                },
                stream: {
                  llseek: MEMFS.stream_ops.llseek,
                  read: MEMFS.stream_ops.read,
                  write: MEMFS.stream_ops.write,
                  allocate: MEMFS.stream_ops.allocate,
                  mmap: MEMFS.stream_ops.mmap,
                  msync: MEMFS.stream_ops.msync
                }
              },
              link: {
                node: {
                  getattr: MEMFS.node_ops.getattr,
                  setattr: MEMFS.node_ops.setattr,
                  readlink: MEMFS.node_ops.readlink
                },
                stream: {}
              },
              chrdev: {
                node: {
                  getattr: MEMFS.node_ops.getattr,
                  setattr: MEMFS.node_ops.setattr
                },
                stream: FS.chrdev_stream_ops
              }
            };
            var node = FS.createNode(parent, name, mode, dev);
            if (FS.isDir(node.mode)) {
              node.node_ops = MEMFS.ops_table.dir.node;
              node.stream_ops = MEMFS.ops_table.dir.stream;
              node.contents = {};
            } else if (FS.isFile(node.mode)) {
              node.node_ops = MEMFS.ops_table.file.node;
              node.stream_ops = MEMFS.ops_table.file.stream;
              node.usedBytes = 0; // The actual number of bytes used in the typed array, as opposed to contents.length which gives the whole capacity.
              // When the byte data of the file is populated, this will point to either a typed array, or a normal JS array. Typed arrays are preferred
              // for performance, and used by default. However, typed arrays are not resizable like normal JS arrays are, so there is a small disk size
              // penalty involved for appending file writes that continuously grow a file similar to std::vector capacity vs used -scheme.
              node.contents = null; 
            } else if (FS.isLink(node.mode)) {
              node.node_ops = MEMFS.ops_table.link.node;
              node.stream_ops = MEMFS.ops_table.link.stream;
            } else if (FS.isChrdev(node.mode)) {
              node.node_ops = MEMFS.ops_table.chrdev.node;
              node.stream_ops = MEMFS.ops_table.chrdev.stream;
            }
            node.timestamp = Date.now();
            // add the new node to the parent
            if (parent) {
              parent.contents[name] = node;
              parent.timestamp = node.timestamp;
            }
            return node;
          },
      getFileDataAsTypedArray(node) {
            if (!node.contents) return new Uint8Array(0);
            if (node.contents.subarray) return node.contents.subarray(0, node.usedBytes); // Make sure to not return excess unused bytes.
            return new Uint8Array(node.contents);
          },
      expandFileStorage(node, newCapacity) {
            var prevCapacity = node.contents ? node.contents.length : 0;
            if (prevCapacity >= newCapacity) return; // No need to expand, the storage was already large enough.
            // Don't expand strictly to the given requested limit if it's only a very small increase, but instead geometrically grow capacity.
            // For small filesizes (<1MB), perform size*2 geometric increase, but for large sizes, do a much more conservative size*1.125 increase to
            // avoid overshooting the allocation cap by a very large margin.
            var CAPACITY_DOUBLING_MAX = 1024 * 1024;
            newCapacity = Math.max(newCapacity, (prevCapacity * (prevCapacity < CAPACITY_DOUBLING_MAX ? 2.0 : 1.125)) >>> 0);
            if (prevCapacity != 0) newCapacity = Math.max(newCapacity, 256); // At minimum allocate 256b for each file when expanding.
            var oldContents = node.contents;
            node.contents = new Uint8Array(newCapacity); // Allocate new storage.
            if (node.usedBytes > 0) node.contents.set(oldContents.subarray(0, node.usedBytes), 0); // Copy old data over to the new storage.
          },
      resizeFileStorage(node, newSize) {
            if (node.usedBytes == newSize) return;
            if (newSize == 0) {
              node.contents = null; // Fully decommit when requesting a resize to zero.
              node.usedBytes = 0;
            } else {
              var oldContents = node.contents;
              node.contents = new Uint8Array(newSize); // Allocate new storage.
              if (oldContents) {
                node.contents.set(oldContents.subarray(0, Math.min(newSize, node.usedBytes))); // Copy old data over to the new storage.
              }
              node.usedBytes = newSize;
            }
          },
      node_ops:{
      getattr(node) {
              var attr = {};
              // device numbers reuse inode numbers.
              attr.dev = FS.isChrdev(node.mode) ? node.id : 1;
              attr.ino = node.id;
              attr.mode = node.mode;
              attr.nlink = 1;
              attr.uid = 0;
              attr.gid = 0;
              attr.rdev = node.rdev;
              if (FS.isDir(node.mode)) {
                attr.size = 4096;
              } else if (FS.isFile(node.mode)) {
                attr.size = node.usedBytes;
              } else if (FS.isLink(node.mode)) {
                attr.size = node.link.length;
              } else {
                attr.size = 0;
              }
              attr.atime = new Date(node.timestamp);
              attr.mtime = new Date(node.timestamp);
              attr.ctime = new Date(node.timestamp);
              // NOTE: In our implementation, st_blocks = Math.ceil(st_size/st_blksize),
              //       but this is not required by the standard.
              attr.blksize = 4096;
              attr.blocks = Math.ceil(attr.size / attr.blksize);
              return attr;
            },
      setattr(node, attr) {
              if (attr.mode !== undefined) {
                node.mode = attr.mode;
              }
              if (attr.timestamp !== undefined) {
                node.timestamp = attr.timestamp;
              }
              if (attr.size !== undefined) {
                MEMFS.resizeFileStorage(node, attr.size);
              }
            },
      lookup(parent, name) {
              throw FS.genericErrors[44];
            },
      mknod(parent, name, mode, dev) {
              return MEMFS.createNode(parent, name, mode, dev);
            },
      rename(old_node, new_dir, new_name) {
              // if we're overwriting a directory at new_name, make sure it's empty.
              if (FS.isDir(old_node.mode)) {
                var new_node;
                try {
                  new_node = FS.lookupNode(new_dir, new_name);
                } catch (e) {
                }
                if (new_node) {
                  for (var i in new_node.contents) {
                    throw new FS.ErrnoError(55);
                  }
                }
              }
              // do the internal rewiring
              delete old_node.parent.contents[old_node.name];
              old_node.parent.timestamp = Date.now();
              old_node.name = new_name;
              new_dir.contents[new_name] = old_node;
              new_dir.timestamp = old_node.parent.timestamp;
              old_node.parent = new_dir;
            },
      unlink(parent, name) {
              delete parent.contents[name];
              parent.timestamp = Date.now();
            },
      rmdir(parent, name) {
              var node = FS.lookupNode(parent, name);
              for (var i in node.contents) {
                throw new FS.ErrnoError(55);
              }
              delete parent.contents[name];
              parent.timestamp = Date.now();
            },
      readdir(node) {
              var entries = ['.', '..'];
              for (var key of Object.keys(node.contents)) {
                entries.push(key);
              }
              return entries;
            },
      symlink(parent, newname, oldpath) {
              var node = MEMFS.createNode(parent, newname, 511 /* 0777 */ | 40960, 0);
              node.link = oldpath;
              return node;
            },
      readlink(node) {
              if (!FS.isLink(node.mode)) {
                throw new FS.ErrnoError(28);
              }
              return node.link;
            },
      },
      stream_ops:{
      read(stream, buffer, offset, length, position) {
              var contents = stream.node.contents;
              if (position >= stream.node.usedBytes) return 0;
              var size = Math.min(stream.node.usedBytes - position, length);
              if (size > 8 && contents.subarray) { // non-trivial, and typed array
                buffer.set(contents.subarray(position, position + size), offset);
              } else {
                for (var i = 0; i < size; i++) buffer[offset + i] = contents[position + i];
              }
              return size;
            },
      write(stream, buffer, offset, length, position, canOwn) {
              // If the buffer is located in main memory (HEAP), and if
              // memory can grow, we can't hold on to references of the
              // memory buffer, as they may get invalidated. That means we
              // need to do copy its contents.
              if (buffer.buffer === HEAP8.buffer) {
                canOwn = false;
              }
      
              if (!length) return 0;
              var node = stream.node;
              node.timestamp = Date.now();
      
              if (buffer.subarray && (!node.contents || node.contents.subarray)) { // This write is from a typed array to a typed array?
                if (canOwn) {
                  node.contents = buffer.subarray(offset, offset + length);
                  node.usedBytes = length;
                  return length;
                } else if (node.usedBytes === 0 && position === 0) { // If this is a simple first write to an empty file, do a fast set since we don't need to care about old data.
                  node.contents = buffer.slice(offset, offset + length);
                  node.usedBytes = length;
                  return length;
                } else if (position + length <= node.usedBytes) { // Writing to an already allocated and used subrange of the file?
                  node.contents.set(buffer.subarray(offset, offset + length), position);
                  return length;
                }
              }
      
              // Appending to an existing file and we need to reallocate, or source data did not come as a typed array.
              MEMFS.expandFileStorage(node, position+length);
              if (node.contents.subarray && buffer.subarray) {
                // Use typed array write which is available.
                node.contents.set(buffer.subarray(offset, offset + length), position);
              } else {
                for (var i = 0; i < length; i++) {
                 node.contents[position + i] = buffer[offset + i]; // Or fall back to manual write if not.
                }
              }
              node.usedBytes = Math.max(node.usedBytes, position + length);
              return length;
            },
      llseek(stream, offset, whence) {
              var position = offset;
              if (whence === 1) {
                position += stream.position;
              } else if (whence === 2) {
                if (FS.isFile(stream.node.mode)) {
                  position += stream.node.usedBytes;
                }
              }
              if (position < 0) {
                throw new FS.ErrnoError(28);
              }
              return position;
            },
      allocate(stream, offset, length) {
              MEMFS.expandFileStorage(stream.node, offset + length);
              stream.node.usedBytes = Math.max(stream.node.usedBytes, offset + length);
            },
      mmap(stream, length, position, prot, flags) {
              if (!FS.isFile(stream.node.mode)) {
                throw new FS.ErrnoError(43);
              }
              var ptr;
              var allocated;
              var contents = stream.node.contents;
              // Only make a new copy when MAP_PRIVATE is specified.
              if (!(flags & 2) && contents.buffer === HEAP8.buffer) {
                // We can't emulate MAP_SHARED when the file is not backed by the
                // buffer we're mapping to (e.g. the HEAP buffer).
                allocated = false;
                ptr = contents.byteOffset;
              } else {
                // Try to avoid unnecessary slices.
                if (position > 0 || position + length < contents.length) {
                  if (contents.subarray) {
                    contents = contents.subarray(position, position + length);
                  } else {
                    contents = Array.prototype.slice.call(contents, position, position + length);
                  }
                }
                allocated = true;
                ptr = mmapAlloc(length);
                if (!ptr) {
                  throw new FS.ErrnoError(48);
                }
                HEAP8.set(contents, ptr);
              }
              return { ptr, allocated };
            },
      msync(stream, buffer, offset, length, mmapFlags) {
              MEMFS.stream_ops.write(stream, buffer, 0, length, offset, false);
              // should we check if bytesWritten and length are the same?
              return 0;
            },
      },
      };
      
      /** @param {boolean=} noRunDep */
      var asyncLoad = (url, onload, onerror, noRunDep) => {
          var dep = getUniqueRunDependency(`al ${url}`) ;
          readAsync(url, (arrayBuffer) => {
            onload(new Uint8Array(arrayBuffer));
            if (dep) removeRunDependency();
          }, (event) => {
            if (onerror) {
              onerror();
            } else {
              throw `Loading data file "${url}" failed.`;
            }
          });
          if (dep) addRunDependency();
        };
      
      
      var FS_createDataFile = (parent, name, fileData, canRead, canWrite, canOwn) => {
          FS.createDataFile(parent, name, fileData, canRead, canWrite, canOwn);
        };
      
      var preloadPlugins = Module['preloadPlugins'] || [];
      var FS_handledByPreloadPlugin = (byteArray, fullname, finish, onerror) => {
          // Ensure plugins are ready.
          if (typeof Browser != 'undefined') Browser.init();
      
          var handled = false;
          preloadPlugins.forEach((plugin) => {
            if (handled) return;
            if (plugin['canHandle'](fullname)) {
              plugin['handle'](byteArray, fullname, finish, onerror);
              handled = true;
            }
          });
          return handled;
        };
      var FS_createPreloadedFile = (parent, name, url, canRead, canWrite, onload, onerror, dontCreateFile, canOwn, preFinish) => {
          // TODO we should allow people to just pass in a complete filename instead
          // of parent and name being that we just join them anyways
          var fullname = name ? PATH_FS.resolve(PATH.join2(parent, name)) : parent;
          function processData(byteArray) {
            function finish(byteArray) {
              preFinish?.();
              if (!dontCreateFile) {
                FS_createDataFile(parent, name, byteArray, canRead, canWrite, canOwn);
              }
              onload?.();
              removeRunDependency();
            }
            if (FS_handledByPreloadPlugin(byteArray, fullname, finish, () => {
              onerror?.();
              removeRunDependency();
            })) {
              return;
            }
            finish(byteArray);
          }
          addRunDependency();
          if (typeof url == 'string') {
            asyncLoad(url, processData, onerror);
          } else {
            processData(url);
          }
        };
      
      var FS_modeStringToFlags = (str) => {
          var flagModes = {
            'r': 0,
            'r+': 2,
            'w': 512 | 64 | 1,
            'w+': 512 | 64 | 2,
            'a': 1024 | 64 | 1,
            'a+': 1024 | 64 | 2,
          };
          var flags = flagModes[str];
          if (typeof flags == 'undefined') {
            throw new Error(`Unknown file open mode: ${str}`);
          }
          return flags;
        };
      
      var FS_getMode = (canRead, canWrite) => {
          var mode = 0;
          if (canRead) mode |= 292 | 73;
          if (canWrite) mode |= 146;
          return mode;
        };
      
      
      
      var FS = {
      root:null,
      mounts:[],
      devices:{
      },
      streams:[],
      nextInode:1,
      nameTable:null,
      currentPath:"/",
      initialized:false,
      ignorePermissions:true,
      ErrnoError:class {
            // We set the `name` property to be able to identify `FS.ErrnoError`
            // - the `name` is a standard ECMA-262 property of error objects. Kind of good to have it anyway.
            // - when using PROXYFS, an error can come from an underlying FS
            // as different FS objects have their own FS.ErrnoError each,
            // the test `err instanceof FS.ErrnoError` won't detect an error coming from another filesystem, causing bugs.
            // we'll use the reliable test `err.name == "ErrnoError"` instead
            constructor(errno) {
              // TODO(sbc): Use the inline member declaration syntax once we
              // support it in acorn and closure.
              this.name = 'ErrnoError';
              this.errno = errno;
            }
          },
      genericErrors:{
      },
      filesystems:null,
      syncFSRequests:0,
      FSStream:class {
            constructor() {
              // TODO(https://github.com/emscripten-core/emscripten/issues/21414):
              // Use inline field declarations.
              this.shared = {};
            }
            get object() {
              return this.node;
            }
            set object(val) {
              this.node = val;
            }
            get isRead() {
              return (this.flags & 2097155) !== 1;
            }
            get isWrite() {
              return (this.flags & 2097155) !== 0;
            }
            get isAppend() {
              return (this.flags & 1024);
            }
            get flags() {
              return this.shared.flags;
            }
            set flags(val) {
              this.shared.flags = val;
            }
            get position() {
              return this.shared.position;
            }
            set position(val) {
              this.shared.position = val;
            }
          },
      FSNode:class {
            constructor(parent, name, mode, rdev) {
              if (!parent) {
                parent = this;  // root node sets parent to itself
              }
              this.parent = parent;
              this.mount = parent.mount;
              this.mounted = null;
              this.id = FS.nextInode++;
              this.name = name;
              this.mode = mode;
              this.node_ops = {};
              this.stream_ops = {};
              this.rdev = rdev;
              this.readMode = 292/*292*/ | 73/*73*/;
              this.writeMode = 146/*146*/;
            }
            get read() {
              return (this.mode & this.readMode) === this.readMode;
            }
            set read(val) {
              val ? this.mode |= this.readMode : this.mode &= ~this.readMode;
            }
            get write() {
              return (this.mode & this.writeMode) === this.writeMode;
            }
            set write(val) {
              val ? this.mode |= this.writeMode : this.mode &= ~this.writeMode;
            }
            get isFolder() {
              return FS.isDir(this.mode);
            }
            get isDevice() {
              return FS.isChrdev(this.mode);
            }
          },
      lookupPath(path, opts = {}) {
            path = PATH_FS.resolve(path);
      
            if (!path) return { path: '', node: null };
      
            var defaults = {
              follow_mount: true,
              recurse_count: 0
            };
            opts = Object.assign(defaults, opts);
      
            if (opts.recurse_count > 8) {  // max recursive lookup of 8
              throw new FS.ErrnoError(32);
            }
      
            // split the absolute path
            var parts = path.split('/').filter((p) => !!p);
      
            // start at the root
            var current = FS.root;
            var current_path = '/';
      
            for (var i = 0; i < parts.length; i++) {
              var islast = (i === parts.length-1);
              if (islast && opts.parent) {
                // stop resolving
                break;
              }
      
              current = FS.lookupNode(current, parts[i]);
              current_path = PATH.join2(current_path, parts[i]);
      
              // jump to the mount's root node if this is a mountpoint
              if (FS.isMountpoint(current)) {
                if (!islast || (islast && opts.follow_mount)) {
                  current = current.mounted.root;
                }
              }
      
              // by default, lookupPath will not follow a symlink if it is the final path component.
              // setting opts.follow = true will override this behavior.
              if (!islast || opts.follow) {
                var count = 0;
                while (FS.isLink(current.mode)) {
                  var link = FS.readlink(current_path);
                  current_path = PATH_FS.resolve(PATH.dirname(current_path), link);
      
                  var lookup = FS.lookupPath(current_path, { recurse_count: opts.recurse_count + 1 });
                  current = lookup.node;
      
                  if (count++ > 40) {  // limit max consecutive symlinks to 40 (SYMLOOP_MAX).
                    throw new FS.ErrnoError(32);
                  }
                }
              }
            }
      
            return { path: current_path, node: current };
          },
      getPath(node) {
            var path;
            while (true) {
              if (FS.isRoot(node)) {
                var mount = node.mount.mountpoint;
                if (!path) return mount;
                return mount[mount.length-1] !== '/' ? `${mount}/${path}` : mount + path;
              }
              path = path ? `${node.name}/${path}` : node.name;
              node = node.parent;
            }
          },
      hashName(parentid, name) {
            var hash = 0;
      
            for (var i = 0; i < name.length; i++) {
              hash = ((hash << 5) - hash + name.charCodeAt(i)) | 0;
            }
            return ((parentid + hash) >>> 0) % FS.nameTable.length;
          },
      hashAddNode(node) {
            var hash = FS.hashName(node.parent.id, node.name);
            node.name_next = FS.nameTable[hash];
            FS.nameTable[hash] = node;
          },
      hashRemoveNode(node) {
            var hash = FS.hashName(node.parent.id, node.name);
            if (FS.nameTable[hash] === node) {
              FS.nameTable[hash] = node.name_next;
            } else {
              var current = FS.nameTable[hash];
              while (current) {
                if (current.name_next === node) {
                  current.name_next = node.name_next;
                  break;
                }
                current = current.name_next;
              }
            }
          },
      lookupNode(parent, name) {
            var errCode = FS.mayLookup(parent);
            if (errCode) {
              throw new FS.ErrnoError(errCode);
            }
            var hash = FS.hashName(parent.id, name);
            for (var node = FS.nameTable[hash]; node; node = node.name_next) {
              var nodeName = node.name;
              if (node.parent.id === parent.id && nodeName === name) {
                return node;
              }
            }
            // if we failed to find it in the cache, call into the VFS
            return FS.lookup(parent, name);
          },
      createNode(parent, name, mode, rdev) {
            var node = new FS.FSNode(parent, name, mode, rdev);
      
            FS.hashAddNode(node);
      
            return node;
          },
      destroyNode(node) {
            FS.hashRemoveNode(node);
          },
      isRoot(node) {
            return node === node.parent;
          },
      isMountpoint(node) {
            return !!node.mounted;
          },
      isFile(mode) {
            return (mode & 61440) === 32768;
          },
      isDir(mode) {
            return (mode & 61440) === 16384;
          },
      isLink(mode) {
            return (mode & 61440) === 40960;
          },
      isChrdev(mode) {
            return (mode & 61440) === 8192;
          },
      isBlkdev(mode) {
            return (mode & 61440) === 24576;
          },
      isFIFO(mode) {
            return (mode & 61440) === 4096;
          },
      isSocket(mode) {
            return (mode & 49152) === 49152;
          },
      flagsToPermissionString(flag) {
            var perms = ['r', 'w', 'rw'][flag & 3];
            if ((flag & 512)) {
              perms += 'w';
            }
            return perms;
          },
      nodePermissions(node, perms) {
            if (FS.ignorePermissions) {
              return 0;
            }
            // return 0 if any user, group or owner bits are set.
            if (perms.includes('r') && !(node.mode & 292)) {
              return 2;
            } else if (perms.includes('w') && !(node.mode & 146)) {
              return 2;
            } else if (perms.includes('x') && !(node.mode & 73)) {
              return 2;
            }
            return 0;
          },
      mayLookup(dir) {
            if (!FS.isDir(dir.mode)) return 54;
            var errCode = FS.nodePermissions(dir, 'x');
            if (errCode) return errCode;
            if (!dir.node_ops.lookup) return 2;
            return 0;
          },
      mayCreate(dir, name) {
            try {
              var node = FS.lookupNode(dir, name);
              return 20;
            } catch (e) {
            }
            return FS.nodePermissions(dir, 'wx');
          },
      mayDelete(dir, name, isdir) {
            var node;
            try {
              node = FS.lookupNode(dir, name);
            } catch (e) {
              return e.errno;
            }
            var errCode = FS.nodePermissions(dir, 'wx');
            if (errCode) {
              return errCode;
            }
            if (isdir) {
              if (!FS.isDir(node.mode)) {
                return 54;
              }
              if (FS.isRoot(node) || FS.getPath(node) === FS.cwd()) {
                return 10;
              }
            } else {
              if (FS.isDir(node.mode)) {
                return 31;
              }
            }
            return 0;
          },
      mayOpen(node, flags) {
            if (!node) {
              return 44;
            }
            if (FS.isLink(node.mode)) {
              return 32;
            } else if (FS.isDir(node.mode)) {
              if (FS.flagsToPermissionString(flags) !== 'r' || // opening for write
                  (flags & 512)) { // TODO: check for O_SEARCH? (== search for dir only)
                return 31;
              }
            }
            return FS.nodePermissions(node, FS.flagsToPermissionString(flags));
          },
      MAX_OPEN_FDS:4096,
      nextfd() {
            for (var fd = 0; fd <= FS.MAX_OPEN_FDS; fd++) {
              if (!FS.streams[fd]) {
                return fd;
              }
            }
            throw new FS.ErrnoError(33);
          },
      getStreamChecked(fd) {
            var stream = FS.getStream(fd);
            if (!stream) {
              throw new FS.ErrnoError(8);
            }
            return stream;
          },
      getStream:(fd) => FS.streams[fd],
      createStream(stream, fd = -1) {
      
            // clone it, so we can return an instance of FSStream
            stream = Object.assign(new FS.FSStream(), stream);
            if (fd == -1) {
              fd = FS.nextfd();
            }
            stream.fd = fd;
            FS.streams[fd] = stream;
            return stream;
          },
      closeStream(fd) {
            FS.streams[fd] = null;
          },
      dupStream(origStream, fd = -1) {
            var stream = FS.createStream(origStream, fd);
            stream.stream_ops?.dup?.(stream);
            return stream;
          },
      chrdev_stream_ops:{
      open(stream) {
              var device = FS.getDevice(stream.node.rdev);
              // override node's stream ops with the device's
              stream.stream_ops = device.stream_ops;
              // forward the open call
              stream.stream_ops.open?.(stream);
            },
      llseek() {
              throw new FS.ErrnoError(70);
            },
      },
      major:(dev) => ((dev) >> 8),
      minor:(dev) => ((dev) & 0xff),
      makedev:(ma, mi) => ((ma) << 8 | (mi)),
      registerDevice(dev, ops) {
            FS.devices[dev] = { stream_ops: ops };
          },
      getDevice:(dev) => FS.devices[dev],
      getMounts(mount) {
            var mounts = [];
            var check = [mount];
      
            while (check.length) {
              var m = check.pop();
      
              mounts.push(m);
      
              check.push(...m.mounts);
            }
      
            return mounts;
          },
      syncfs(populate, callback) {
            if (typeof populate == 'function') {
              callback = populate;
              populate = false;
            }
      
            FS.syncFSRequests++;
      
            if (FS.syncFSRequests > 1) {
              err(`warning: ${FS.syncFSRequests} FS.syncfs operations in flight at once, probably just doing extra work`);
            }
      
            var mounts = FS.getMounts(FS.root.mount);
            var completed = 0;
      
            function doCallback(errCode) {
              FS.syncFSRequests--;
              return callback(errCode);
            }
      
            function done(errCode) {
              if (errCode) {
                if (!done.errored) {
                  done.errored = true;
                  return doCallback(errCode);
                }
                return;
              }
              if (++completed >= mounts.length) {
                doCallback(null);
              }
            }  
            // sync all mounts
            mounts.forEach((mount) => {
              if (!mount.type.syncfs) {
                return done(null);
              }
              mount.type.syncfs(mount, populate, done);
            });
          },
      mount(type, opts, mountpoint) {
            var root = mountpoint === '/';
            var pseudo = !mountpoint;
            var node;
      
            if (root && FS.root) {
              throw new FS.ErrnoError(10);
            } else if (!root && !pseudo) {
              var lookup = FS.lookupPath(mountpoint, { follow_mount: false });
      
              mountpoint = lookup.path;  // use the absolute path
              node = lookup.node;
      
              if (FS.isMountpoint(node)) {
                throw new FS.ErrnoError(10);
              }
      
              if (!FS.isDir(node.mode)) {
                throw new FS.ErrnoError(54);
              }
            }
      
            var mount = {
              type,
              opts,
              mountpoint,
              mounts: []
            };
      
            // create a root node for the fs
            var mountRoot = type.mount(mount);
            mountRoot.mount = mount;
            mount.root = mountRoot;
      
            if (root) {
              FS.root = mountRoot;
            } else if (node) {
              // set as a mountpoint
              node.mounted = mount;
      
              // add the new mount to the current mount's children
              if (node.mount) {
                node.mount.mounts.push(mount);
              }
            }
      
            return mountRoot;
          },
      unmount(mountpoint) {
            var lookup = FS.lookupPath(mountpoint, { follow_mount: false });
      
            if (!FS.isMountpoint(lookup.node)) {
              throw new FS.ErrnoError(28);
            }
      
            // destroy the nodes for this mount, and all its child mounts
            var node = lookup.node;
            var mount = node.mounted;
            var mounts = FS.getMounts(mount);
      
            Object.keys(FS.nameTable).forEach((hash) => {
              var current = FS.nameTable[hash];
      
              while (current) {
                var next = current.name_next;
      
                if (mounts.includes(current.mount)) {
                  FS.destroyNode(current);
                }
      
                current = next;
              }
            });
      
            // no longer a mountpoint
            node.mounted = null;
      
            // remove this mount from the child mounts
            var idx = node.mount.mounts.indexOf(mount);
            node.mount.mounts.splice(idx, 1);
          },
      lookup(parent, name) {
            return parent.node_ops.lookup(parent, name);
          },
      mknod(path, mode, dev) {
            var lookup = FS.lookupPath(path, { parent: true });
            var parent = lookup.node;
            var name = PATH.basename(path);
            if (!name || name === '.' || name === '..') {
              throw new FS.ErrnoError(28);
            }
            var errCode = FS.mayCreate(parent, name);
            if (errCode) {
              throw new FS.ErrnoError(errCode);
            }
            if (!parent.node_ops.mknod) {
              throw new FS.ErrnoError(63);
            }
            return parent.node_ops.mknod(parent, name, mode, dev);
          },
      create(path, mode) {
            mode = mode !== undefined ? mode : 438 /* 0666 */;
            mode &= 4095;
            mode |= 32768;
            return FS.mknod(path, mode, 0);
          },
      mkdir(path, mode) {
            mode = mode !== undefined ? mode : 511 /* 0777 */;
            mode &= 511 | 512;
            mode |= 16384;
            return FS.mknod(path, mode, 0);
          },
      mkdirTree(path, mode) {
            var dirs = path.split('/');
            var d = '';
            for (var i = 0; i < dirs.length; ++i) {
              if (!dirs[i]) continue;
              d += '/' + dirs[i];
              try {
                FS.mkdir(d, mode);
              } catch(e) {
                if (e.errno != 20) throw e;
              }
            }
          },
      mkdev(path, mode, dev) {
            if (typeof dev == 'undefined') {
              dev = mode;
              mode = 438 /* 0666 */;
            }
            mode |= 8192;
            return FS.mknod(path, mode, dev);
          },
      symlink(oldpath, newpath) {
            if (!PATH_FS.resolve(oldpath)) {
              throw new FS.ErrnoError(44);
            }
            var lookup = FS.lookupPath(newpath, { parent: true });
            var parent = lookup.node;
            if (!parent) {
              throw new FS.ErrnoError(44);
            }
            var newname = PATH.basename(newpath);
            var errCode = FS.mayCreate(parent, newname);
            if (errCode) {
              throw new FS.ErrnoError(errCode);
            }
            if (!parent.node_ops.symlink) {
              throw new FS.ErrnoError(63);
            }
            return parent.node_ops.symlink(parent, newname, oldpath);
          },
      rename(old_path, new_path) {
            var old_dirname = PATH.dirname(old_path);
            var new_dirname = PATH.dirname(new_path);
            var old_name = PATH.basename(old_path);
            var new_name = PATH.basename(new_path);
            // parents must exist
            var lookup, old_dir, new_dir;
      
            // let the errors from non existent directories percolate up
            lookup = FS.lookupPath(old_path, { parent: true });
            old_dir = lookup.node;
            lookup = FS.lookupPath(new_path, { parent: true });
            new_dir = lookup.node;
      
            if (!old_dir || !new_dir) throw new FS.ErrnoError(44);
            // need to be part of the same mount
            if (old_dir.mount !== new_dir.mount) {
              throw new FS.ErrnoError(75);
            }
            // source must exist
            var old_node = FS.lookupNode(old_dir, old_name);
            // old path should not be an ancestor of the new path
            var relative = PATH_FS.relative(old_path, new_dirname);
            if (relative.charAt(0) !== '.') {
              throw new FS.ErrnoError(28);
            }
            // new path should not be an ancestor of the old path
            relative = PATH_FS.relative(new_path, old_dirname);
            if (relative.charAt(0) !== '.') {
              throw new FS.ErrnoError(55);
            }
            // see if the new path already exists
            var new_node;
            try {
              new_node = FS.lookupNode(new_dir, new_name);
            } catch (e) {
              // not fatal
            }
            // early out if nothing needs to change
            if (old_node === new_node) {
              return;
            }
            // we'll need to delete the old entry
            var isdir = FS.isDir(old_node.mode);
            var errCode = FS.mayDelete(old_dir, old_name, isdir);
            if (errCode) {
              throw new FS.ErrnoError(errCode);
            }
            // need delete permissions if we'll be overwriting.
            // need create permissions if new doesn't already exist.
            errCode = new_node ?
              FS.mayDelete(new_dir, new_name, isdir) :
              FS.mayCreate(new_dir, new_name);
            if (errCode) {
              throw new FS.ErrnoError(errCode);
            }
            if (!old_dir.node_ops.rename) {
              throw new FS.ErrnoError(63);
            }
            if (FS.isMountpoint(old_node) || (new_node && FS.isMountpoint(new_node))) {
              throw new FS.ErrnoError(10);
            }
            // if we are going to change the parent, check write permissions
            if (new_dir !== old_dir) {
              errCode = FS.nodePermissions(old_dir, 'w');
              if (errCode) {
                throw new FS.ErrnoError(errCode);
              }
            }
            // remove the node from the lookup hash
            FS.hashRemoveNode(old_node);
            // do the underlying fs rename
            try {
              old_dir.node_ops.rename(old_node, new_dir, new_name);
            } catch (e) {
              throw e;
            } finally {
              // add the node back to the hash (in case node_ops.rename
              // changed its name)
              FS.hashAddNode(old_node);
            }
          },
      rmdir(path) {
            var lookup = FS.lookupPath(path, { parent: true });
            var parent = lookup.node;
            var name = PATH.basename(path);
            var node = FS.lookupNode(parent, name);
            var errCode = FS.mayDelete(parent, name, true);
            if (errCode) {
              throw new FS.ErrnoError(errCode);
            }
            if (!parent.node_ops.rmdir) {
              throw new FS.ErrnoError(63);
            }
            if (FS.isMountpoint(node)) {
              throw new FS.ErrnoError(10);
            }
            parent.node_ops.rmdir(parent, name);
            FS.destroyNode(node);
          },
      readdir(path) {
            var lookup = FS.lookupPath(path, { follow: true });
            var node = lookup.node;
            if (!node.node_ops.readdir) {
              throw new FS.ErrnoError(54);
            }
            return node.node_ops.readdir(node);
          },
      unlink(path) {
            var lookup = FS.lookupPath(path, { parent: true });
            var parent = lookup.node;
            if (!parent) {
              throw new FS.ErrnoError(44);
            }
            var name = PATH.basename(path);
            var node = FS.lookupNode(parent, name);
            var errCode = FS.mayDelete(parent, name, false);
            if (errCode) {
              // According to POSIX, we should map EISDIR to EPERM, but
              // we instead do what Linux does (and we must, as we use
              // the musl linux libc).
              throw new FS.ErrnoError(errCode);
            }
            if (!parent.node_ops.unlink) {
              throw new FS.ErrnoError(63);
            }
            if (FS.isMountpoint(node)) {
              throw new FS.ErrnoError(10);
            }
            parent.node_ops.unlink(parent, name);
            FS.destroyNode(node);
          },
      readlink(path) {
            var lookup = FS.lookupPath(path);
            var link = lookup.node;
            if (!link) {
              throw new FS.ErrnoError(44);
            }
            if (!link.node_ops.readlink) {
              throw new FS.ErrnoError(28);
            }
            return PATH_FS.resolve(FS.getPath(link.parent), link.node_ops.readlink(link));
          },
      stat(path, dontFollow) {
            var lookup = FS.lookupPath(path, { follow: !dontFollow });
            var node = lookup.node;
            if (!node) {
              throw new FS.ErrnoError(44);
            }
            if (!node.node_ops.getattr) {
              throw new FS.ErrnoError(63);
            }
            return node.node_ops.getattr(node);
          },
      lstat(path) {
            return FS.stat(path, true);
          },
      chmod(path, mode, dontFollow) {
            var node;
            if (typeof path == 'string') {
              var lookup = FS.lookupPath(path, { follow: !dontFollow });
              node = lookup.node;
            } else {
              node = path;
            }
            if (!node.node_ops.setattr) {
              throw new FS.ErrnoError(63);
            }
            node.node_ops.setattr(node, {
              mode: (mode & 4095) | (node.mode & -4096),
              timestamp: Date.now()
            });
          },
      lchmod(path, mode) {
            FS.chmod(path, mode, true);
          },
      fchmod(fd, mode) {
            var stream = FS.getStreamChecked(fd);
            FS.chmod(stream.node, mode);
          },
      chown(path, uid, gid, dontFollow) {
            var node;
            if (typeof path == 'string') {
              var lookup = FS.lookupPath(path, { follow: !dontFollow });
              node = lookup.node;
            } else {
              node = path;
            }
            if (!node.node_ops.setattr) {
              throw new FS.ErrnoError(63);
            }
            node.node_ops.setattr(node, {
              timestamp: Date.now()
              // we ignore the uid / gid for now
            });
          },
      lchown(path, uid, gid) {
            FS.chown(path, uid, gid, true);
          },
      fchown(fd, uid, gid) {
            var stream = FS.getStreamChecked(fd);
            FS.chown(stream.node, uid, gid);
          },
      truncate(path, len) {
            if (len < 0) {
              throw new FS.ErrnoError(28);
            }
            var node;
            if (typeof path == 'string') {
              var lookup = FS.lookupPath(path, { follow: true });
              node = lookup.node;
            } else {
              node = path;
            }
            if (!node.node_ops.setattr) {
              throw new FS.ErrnoError(63);
            }
            if (FS.isDir(node.mode)) {
              throw new FS.ErrnoError(31);
            }
            if (!FS.isFile(node.mode)) {
              throw new FS.ErrnoError(28);
            }
            var errCode = FS.nodePermissions(node, 'w');
            if (errCode) {
              throw new FS.ErrnoError(errCode);
            }
            node.node_ops.setattr(node, {
              size: len,
              timestamp: Date.now()
            });
          },
      ftruncate(fd, len) {
            var stream = FS.getStreamChecked(fd);
            if ((stream.flags & 2097155) === 0) {
              throw new FS.ErrnoError(28);
            }
            FS.truncate(stream.node, len);
          },
      utime(path, atime, mtime) {
            var lookup = FS.lookupPath(path, { follow: true });
            var node = lookup.node;
            node.node_ops.setattr(node, {
              timestamp: Math.max(atime, mtime)
            });
          },
      open(path, flags, mode) {
            if (path === "") {
              throw new FS.ErrnoError(44);
            }
            flags = typeof flags == 'string' ? FS_modeStringToFlags(flags) : flags;
            mode = typeof mode == 'undefined' ? 438 /* 0666 */ : mode;
            if ((flags & 64)) {
              mode = (mode & 4095) | 32768;
            } else {
              mode = 0;
            }
            var node;
            if (typeof path == 'object') {
              node = path;
            } else {
              path = PATH.normalize(path);
              try {
                var lookup = FS.lookupPath(path, {
                  follow: !(flags & 131072)
                });
                node = lookup.node;
              } catch (e) {
                // ignore
              }
            }
            // perhaps we need to create the node
            var created = false;
            if ((flags & 64)) {
              if (node) {
                // if O_CREAT and O_EXCL are set, error out if the node already exists
                if ((flags & 128)) {
                  throw new FS.ErrnoError(20);
                }
              } else {
                // node doesn't exist, try to create it
                node = FS.mknod(path, mode, 0);
                created = true;
              }
            }
            if (!node) {
              throw new FS.ErrnoError(44);
            }
            // can't truncate a device
            if (FS.isChrdev(node.mode)) {
              flags &= -513;
            }
            // if asked only for a directory, then this must be one
            if ((flags & 65536) && !FS.isDir(node.mode)) {
              throw new FS.ErrnoError(54);
            }
            // check permissions, if this is not a file we just created now (it is ok to
            // create and write to a file with read-only permissions; it is read-only
            // for later use)
            if (!created) {
              var errCode = FS.mayOpen(node, flags);
              if (errCode) {
                throw new FS.ErrnoError(errCode);
              }
            }
            // do truncation if necessary
            if ((flags & 512) && !created) {
              FS.truncate(node, 0);
            }
            // we've already handled these, don't pass down to the underlying vfs
            flags &= -131713;
      
            // register the stream with the filesystem
            var stream = FS.createStream({
              node,
              path: FS.getPath(node),  // we want the absolute path to the node
              flags,
              seekable: true,
              position: 0,
              stream_ops: node.stream_ops,
              // used by the file family libc calls (fopen, fwrite, ferror, etc.)
              ungotten: [],
              error: false
            });
            // call the new stream's open function
            if (stream.stream_ops.open) {
              stream.stream_ops.open(stream);
            }
            if (Module['logReadFiles'] && !(flags & 1)) {
              if (!FS.readFiles) FS.readFiles = {};
              if (!(path in FS.readFiles)) {
                FS.readFiles[path] = 1;
              }
            }
            return stream;
          },
      close(stream) {
            if (FS.isClosed(stream)) {
              throw new FS.ErrnoError(8);
            }
            if (stream.getdents) stream.getdents = null; // free readdir state
            try {
              if (stream.stream_ops.close) {
                stream.stream_ops.close(stream);
              }
            } catch (e) {
              throw e;
            } finally {
              FS.closeStream(stream.fd);
            }
            stream.fd = null;
          },
      isClosed(stream) {
            return stream.fd === null;
          },
      llseek(stream, offset, whence) {
            if (FS.isClosed(stream)) {
              throw new FS.ErrnoError(8);
            }
            if (!stream.seekable || !stream.stream_ops.llseek) {
              throw new FS.ErrnoError(70);
            }
            if (whence != 0 && whence != 1 && whence != 2) {
              throw new FS.ErrnoError(28);
            }
            stream.position = stream.stream_ops.llseek(stream, offset, whence);
            stream.ungotten = [];
            return stream.position;
          },
      read(stream, buffer, offset, length, position) {
            if (length < 0 || position < 0) {
              throw new FS.ErrnoError(28);
            }
            if (FS.isClosed(stream)) {
              throw new FS.ErrnoError(8);
            }
            if ((stream.flags & 2097155) === 1) {
              throw new FS.ErrnoError(8);
            }
            if (FS.isDir(stream.node.mode)) {
              throw new FS.ErrnoError(31);
            }
            if (!stream.stream_ops.read) {
              throw new FS.ErrnoError(28);
            }
            var seeking = typeof position != 'undefined';
            if (!seeking) {
              position = stream.position;
            } else if (!stream.seekable) {
              throw new FS.ErrnoError(70);
            }
            var bytesRead = stream.stream_ops.read(stream, buffer, offset, length, position);
            if (!seeking) stream.position += bytesRead;
            return bytesRead;
          },
      write(stream, buffer, offset, length, position, canOwn) {
            if (length < 0 || position < 0) {
              throw new FS.ErrnoError(28);
            }
            if (FS.isClosed(stream)) {
              throw new FS.ErrnoError(8);
            }
            if ((stream.flags & 2097155) === 0) {
              throw new FS.ErrnoError(8);
            }
            if (FS.isDir(stream.node.mode)) {
              throw new FS.ErrnoError(31);
            }
            if (!stream.stream_ops.write) {
              throw new FS.ErrnoError(28);
            }
            if (stream.seekable && stream.flags & 1024) {
              // seek to the end before writing in append mode
              FS.llseek(stream, 0, 2);
            }
            var seeking = typeof position != 'undefined';
            if (!seeking) {
              position = stream.position;
            } else if (!stream.seekable) {
              throw new FS.ErrnoError(70);
            }
            var bytesWritten = stream.stream_ops.write(stream, buffer, offset, length, position, canOwn);
            if (!seeking) stream.position += bytesWritten;
            return bytesWritten;
          },
      allocate(stream, offset, length) {
            if (FS.isClosed(stream)) {
              throw new FS.ErrnoError(8);
            }
            if (offset < 0 || length <= 0) {
              throw new FS.ErrnoError(28);
            }
            if ((stream.flags & 2097155) === 0) {
              throw new FS.ErrnoError(8);
            }
            if (!FS.isFile(stream.node.mode) && !FS.isDir(stream.node.mode)) {
              throw new FS.ErrnoError(43);
            }
            if (!stream.stream_ops.allocate) {
              throw new FS.ErrnoError(138);
            }
            stream.stream_ops.allocate(stream, offset, length);
          },
      mmap(stream, length, position, prot, flags) {
            // User requests writing to file (prot & PROT_WRITE != 0).
            // Checking if we have permissions to write to the file unless
            // MAP_PRIVATE flag is set. According to POSIX spec it is possible
            // to write to file opened in read-only mode with MAP_PRIVATE flag,
            // as all modifications will be visible only in the memory of
            // the current process.
            if ((prot & 2) !== 0
                && (flags & 2) === 0
                && (stream.flags & 2097155) !== 2) {
              throw new FS.ErrnoError(2);
            }
            if ((stream.flags & 2097155) === 1) {
              throw new FS.ErrnoError(2);
            }
            if (!stream.stream_ops.mmap) {
              throw new FS.ErrnoError(43);
            }
            return stream.stream_ops.mmap(stream, length, position, prot, flags);
          },
      msync(stream, buffer, offset, length, mmapFlags) {
            if (!stream.stream_ops.msync) {
              return 0;
            }
            return stream.stream_ops.msync(stream, buffer, offset, length, mmapFlags);
          },
      ioctl(stream, cmd, arg) {
            if (!stream.stream_ops.ioctl) {
              throw new FS.ErrnoError(59);
            }
            return stream.stream_ops.ioctl(stream, cmd, arg);
          },
      readFile(path, opts = {}) {
            opts.flags = opts.flags || 0;
            opts.encoding = opts.encoding || 'binary';
            if (opts.encoding !== 'utf8' && opts.encoding !== 'binary') {
              throw new Error(`Invalid encoding type "${opts.encoding}"`);
            }
            var ret;
            var stream = FS.open(path, opts.flags);
            var stat = FS.stat(path);
            var length = stat.size;
            var buf = new Uint8Array(length);
            FS.read(stream, buf, 0, length, 0);
            if (opts.encoding === 'utf8') {
              ret = UTF8ArrayToString(buf, 0);
            } else if (opts.encoding === 'binary') {
              ret = buf;
            }
            FS.close(stream);
            return ret;
          },
      writeFile(path, data, opts = {}) {
            opts.flags = opts.flags || 577;
            var stream = FS.open(path, opts.flags, opts.mode);
            if (typeof data == 'string') {
              var buf = new Uint8Array(lengthBytesUTF8(data)+1);
              var actualNumBytes = stringToUTF8Array(data, buf, 0, buf.length);
              FS.write(stream, buf, 0, actualNumBytes, undefined, opts.canOwn);
            } else if (ArrayBuffer.isView(data)) {
              FS.write(stream, data, 0, data.byteLength, undefined, opts.canOwn);
            } else {
              throw new Error('Unsupported data type');
            }
            FS.close(stream);
          },
      cwd:() => FS.currentPath,
      chdir(path) {
            var lookup = FS.lookupPath(path, { follow: true });
            if (lookup.node === null) {
              throw new FS.ErrnoError(44);
            }
            if (!FS.isDir(lookup.node.mode)) {
              throw new FS.ErrnoError(54);
            }
            var errCode = FS.nodePermissions(lookup.node, 'x');
            if (errCode) {
              throw new FS.ErrnoError(errCode);
            }
            FS.currentPath = lookup.path;
          },
      createDefaultDirectories() {
            FS.mkdir('/tmp');
            FS.mkdir('/home');
            FS.mkdir('/home/web_user');
          },
      createDefaultDevices() {
            // create /dev
            FS.mkdir('/dev');
            // setup /dev/null
            FS.registerDevice(FS.makedev(1, 3), {
              read: () => 0,
              write: (stream, buffer, offset, length, pos) => length,
            });
            FS.mkdev('/dev/null', FS.makedev(1, 3));
            // setup /dev/tty and /dev/tty1
            // stderr needs to print output using err() rather than out()
            // so we register a second tty just for it.
            TTY.register(FS.makedev(5, 0), TTY.default_tty_ops);
            TTY.register(FS.makedev(6, 0), TTY.default_tty1_ops);
            FS.mkdev('/dev/tty', FS.makedev(5, 0));
            FS.mkdev('/dev/tty1', FS.makedev(6, 0));
            // setup /dev/[u]random
            // use a buffer to avoid overhead of individual crypto calls per byte
            var randomBuffer = new Uint8Array(1024), randomLeft = 0;
            var randomByte = () => {
              if (randomLeft === 0) {
                randomLeft = randomFill(randomBuffer).byteLength;
              }
              return randomBuffer[--randomLeft];
            };
            FS.createDevice('/dev', 'random', randomByte);
            FS.createDevice('/dev', 'urandom', randomByte);
            // we're not going to emulate the actual shm device,
            // just create the tmp dirs that reside in it commonly
            FS.mkdir('/dev/shm');
            FS.mkdir('/dev/shm/tmp');
          },
      createSpecialDirectories() {
            // create /proc/self/fd which allows /proc/self/fd/6 => readlink gives the
            // name of the stream for fd 6 (see test_unistd_ttyname)
            FS.mkdir('/proc');
            var proc_self = FS.mkdir('/proc/self');
            FS.mkdir('/proc/self/fd');
            FS.mount({
              mount() {
                var node = FS.createNode(proc_self, 'fd', 16384 | 511 /* 0777 */, 73);
                node.node_ops = {
                  lookup(parent, name) {
                    var fd = +name;
                    var stream = FS.getStreamChecked(fd);
                    var ret = {
                      parent: null,
                      mount: { mountpoint: 'fake' },
                      node_ops: { readlink: () => stream.path },
                    };
                    ret.parent = ret; // make it look like a simple root node
                    return ret;
                  }
                };
                return node;
              }
            }, {}, '/proc/self/fd');
          },
      createStandardStreams() {
            // TODO deprecate the old functionality of a single
            // input / output callback and that utilizes FS.createDevice
            // and instead require a unique set of stream ops
      
            // by default, we symlink the standard streams to the
            // default tty devices. however, if the standard streams
            // have been overwritten we create a unique device for
            // them instead.
            if (Module['stdin']) {
              FS.createDevice('/dev', 'stdin', Module['stdin']);
            } else {
              FS.symlink('/dev/tty', '/dev/stdin');
            }
            if (Module['stdout']) {
              FS.createDevice('/dev', 'stdout', null, Module['stdout']);
            } else {
              FS.symlink('/dev/tty', '/dev/stdout');
            }
            if (Module['stderr']) {
              FS.createDevice('/dev', 'stderr', null, Module['stderr']);
            } else {
              FS.symlink('/dev/tty1', '/dev/stderr');
            }
      
            // open default streams for the stdin, stdout and stderr devices
            FS.open('/dev/stdin', 0);
            FS.open('/dev/stdout', 1);
            FS.open('/dev/stderr', 1);
          },
      staticInit() {
            // Some errors may happen quite a bit, to avoid overhead we reuse them (and suffer a lack of stack info)
            [44].forEach((code) => {
              FS.genericErrors[code] = new FS.ErrnoError(code);
              FS.genericErrors[code].stack = '<generic error, no stack>';
            });
      
            FS.nameTable = new Array(4096);
      
            FS.mount(MEMFS, {}, '/');
      
            FS.createDefaultDirectories();
            FS.createDefaultDevices();
            FS.createSpecialDirectories();
      
            FS.filesystems = {
              'MEMFS': MEMFS,
            };
          },
      init(input, output, error) {
            FS.init.initialized = true;
      
            // Allow Module.stdin etc. to provide defaults, if none explicitly passed to us here
            Module['stdin'] = input || Module['stdin'];
            Module['stdout'] = output || Module['stdout'];
            Module['stderr'] = error || Module['stderr'];
      
            FS.createStandardStreams();
          },
      quit() {
            FS.init.initialized = false;
            // force-flush all streams, so we get musl std streams printed out
            _fflush(0);
            // close all of our streams
            for (var i = 0; i < FS.streams.length; i++) {
              var stream = FS.streams[i];
              if (!stream) {
                continue;
              }
              FS.close(stream);
            }
          },
      findObject(path, dontResolveLastLink) {
            var ret = FS.analyzePath(path, dontResolveLastLink);
            if (!ret.exists) {
              return null;
            }
            return ret.object;
          },
      analyzePath(path, dontResolveLastLink) {
            // operate from within the context of the symlink's target
            try {
              var lookup = FS.lookupPath(path, { follow: !dontResolveLastLink });
              path = lookup.path;
            } catch (e) {
            }
            var ret = {
              isRoot: false, exists: false, error: 0, name: null, path: null, object: null,
              parentExists: false, parentPath: null, parentObject: null
            };
            try {
              var lookup = FS.lookupPath(path, { parent: true });
              ret.parentExists = true;
              ret.parentPath = lookup.path;
              ret.parentObject = lookup.node;
              ret.name = PATH.basename(path);
              lookup = FS.lookupPath(path, { follow: !dontResolveLastLink });
              ret.exists = true;
              ret.path = lookup.path;
              ret.object = lookup.node;
              ret.name = lookup.node.name;
              ret.isRoot = lookup.path === '/';
            } catch (e) {
              ret.error = e.errno;
            }        return ret;
          },
      createPath(parent, path, canRead, canWrite) {
            parent = typeof parent == 'string' ? parent : FS.getPath(parent);
            var parts = path.split('/').reverse();
            while (parts.length) {
              var part = parts.pop();
              if (!part) continue;
              var current = PATH.join2(parent, part);
              try {
                FS.mkdir(current);
              } catch (e) {
                // ignore EEXIST
              }
              parent = current;
            }
            return current;
          },
      createFile(parent, name, properties, canRead, canWrite) {
            var path = PATH.join2(typeof parent == 'string' ? parent : FS.getPath(parent), name);
            var mode = FS_getMode(canRead, canWrite);
            return FS.create(path, mode);
          },
      createDataFile(parent, name, data, canRead, canWrite, canOwn) {
            var path = name;
            if (parent) {
              parent = typeof parent == 'string' ? parent : FS.getPath(parent);
              path = name ? PATH.join2(parent, name) : parent;
            }
            var mode = FS_getMode(canRead, canWrite);
            var node = FS.create(path, mode);
            if (data) {
              if (typeof data == 'string') {
                var arr = new Array(data.length);
                for (var i = 0, len = data.length; i < len; ++i) arr[i] = data.charCodeAt(i);
                data = arr;
              }
              // make sure we can write to the file
              FS.chmod(node, mode | 146);
              var stream = FS.open(node, 577);
              FS.write(stream, data, 0, data.length, 0, canOwn);
              FS.close(stream);
              FS.chmod(node, mode);
            }
          },
      createDevice(parent, name, input, output) {
            var path = PATH.join2(typeof parent == 'string' ? parent : FS.getPath(parent), name);
            var mode = FS_getMode(!!input, !!output);
            if (!FS.createDevice.major) FS.createDevice.major = 64;
            var dev = FS.makedev(FS.createDevice.major++, 0);
            // Create a fake device that a set of stream ops to emulate
            // the old behavior.
            FS.registerDevice(dev, {
              open(stream) {
                stream.seekable = false;
              },
              close(stream) {
                // flush any pending line data
                if (output?.buffer?.length) {
                  output(10);
                }
              },
              read(stream, buffer, offset, length, pos /* ignored */) {
                var bytesRead = 0;
                for (var i = 0; i < length; i++) {
                  var result;
                  try {
                    result = input();
                  } catch (e) {
                    throw new FS.ErrnoError(29);
                  }
                  if (result === undefined && bytesRead === 0) {
                    throw new FS.ErrnoError(6);
                  }
                  if (result === null || result === undefined) break;
                  bytesRead++;
                  buffer[offset+i] = result;
                }
                if (bytesRead) {
                  stream.node.timestamp = Date.now();
                }
                return bytesRead;
              },
              write(stream, buffer, offset, length, pos) {
                for (var i = 0; i < length; i++) {
                  try {
                    output(buffer[offset+i]);
                  } catch (e) {
                    throw new FS.ErrnoError(29);
                  }
                }
                if (length) {
                  stream.node.timestamp = Date.now();
                }
                return i;
              }
            });
            return FS.mkdev(path, mode, dev);
          },
      forceLoadFile(obj) {
            if (obj.isDevice || obj.isFolder || obj.link || obj.contents) return true;
            if (typeof XMLHttpRequest != 'undefined') {
              throw new Error("Lazy loading should have been performed (contents set) in createLazyFile, but it was not. Lazy loading only works in web workers. Use --embed-file or --preload-file in emcc on the main thread.");
            } else if (read_) {
              // Command-line.
              try {
                // WARNING: Can't read binary files in V8's d8 or tracemonkey's js, as
                //          read() will try to parse UTF8.
                obj.contents = intArrayFromString(read_(obj.url), true);
                obj.usedBytes = obj.contents.length;
              } catch (e) {
                throw new FS.ErrnoError(29);
              }
            } else {
              throw new Error('Cannot load without read() or XMLHttpRequest.');
            }
          },
      createLazyFile(parent, name, url, canRead, canWrite) {
            // Lazy chunked Uint8Array (implements get and length from Uint8Array).
            // Actual getting is abstracted away for eventual reuse.
            class LazyUint8Array {
              constructor() {
                this.lengthKnown = false;
                this.chunks = []; // Loaded chunks. Index is the chunk number
              }
              get(idx) {
                if (idx > this.length-1 || idx < 0) {
                  return undefined;
                }
                var chunkOffset = idx % this.chunkSize;
                var chunkNum = (idx / this.chunkSize)|0;
                return this.getter(chunkNum)[chunkOffset];
              }
              setDataGetter(getter) {
                this.getter = getter;
              }
              cacheLength() {
                // Find length
                var xhr = new XMLHttpRequest();
                xhr.open('HEAD', url, false);
                xhr.send(null);
                if (!(xhr.status >= 200 && xhr.status < 300 || xhr.status === 304)) throw new Error("Couldn't load " + url + ". Status: " + xhr.status);
                var datalength = Number(xhr.getResponseHeader("Content-length"));
                var header;
                var hasByteServing = (header = xhr.getResponseHeader("Accept-Ranges")) && header === "bytes";
                var usesGzip = (header = xhr.getResponseHeader("Content-Encoding")) && header === "gzip";
      
                var chunkSize = 1024*1024; // Chunk size in bytes
      
                if (!hasByteServing) chunkSize = datalength;
      
                // Function to get a range from the remote URL.
                var doXHR = (from, to) => {
                  if (from > to) throw new Error("invalid range (" + from + ", " + to + ") or no bytes requested!");
                  if (to > datalength-1) throw new Error("only " + datalength + " bytes available! programmer error!");
      
                  // TODO: Use mozResponseArrayBuffer, responseStream, etc. if available.
                  var xhr = new XMLHttpRequest();
                  xhr.open('GET', url, false);
                  if (datalength !== chunkSize) xhr.setRequestHeader("Range", "bytes=" + from + "-" + to);
      
                  // Some hints to the browser that we want binary data.
                  xhr.responseType = 'arraybuffer';
                  if (xhr.overrideMimeType) {
                    xhr.overrideMimeType('text/plain; charset=x-user-defined');
                  }
      
                  xhr.send(null);
                  if (!(xhr.status >= 200 && xhr.status < 300 || xhr.status === 304)) throw new Error("Couldn't load " + url + ". Status: " + xhr.status);
                  if (xhr.response !== undefined) {
                    return new Uint8Array(/** @type{Array<number>} */(xhr.response || []));
                  }
                  return intArrayFromString(xhr.responseText || '', true);
                };
                var lazyArray = this;
                lazyArray.setDataGetter((chunkNum) => {
                  var start = chunkNum * chunkSize;
                  var end = (chunkNum+1) * chunkSize - 1; // including this byte
                  end = Math.min(end, datalength-1); // if datalength-1 is selected, this is the last block
                  if (typeof lazyArray.chunks[chunkNum] == 'undefined') {
                    lazyArray.chunks[chunkNum] = doXHR(start, end);
                  }
                  if (typeof lazyArray.chunks[chunkNum] == 'undefined') throw new Error('doXHR failed!');
                  return lazyArray.chunks[chunkNum];
                });
      
                if (usesGzip || !datalength) {
                  // if the server uses gzip or doesn't supply the length, we have to download the whole file to get the (uncompressed) length
                  chunkSize = datalength = 1; // this will force getter(0)/doXHR do download the whole file
                  datalength = this.getter(0).length;
                  chunkSize = datalength;
                  out("LazyFiles on gzip forces download of the whole file when length is accessed");
                }
      
                this._length = datalength;
                this._chunkSize = chunkSize;
                this.lengthKnown = true;
              }
              get length() {
                if (!this.lengthKnown) {
                  this.cacheLength();
                }
                return this._length;
              }
              get chunkSize() {
                if (!this.lengthKnown) {
                  this.cacheLength();
                }
                return this._chunkSize;
              }
            }
      
            if (typeof XMLHttpRequest != 'undefined') {
              if (!ENVIRONMENT_IS_WORKER) throw 'Cannot do synchronous binary XHRs outside webworkers in modern browsers. Use --embed-file or --preload-file in emcc';
              var lazyArray = new LazyUint8Array();
              var properties = { isDevice: false, contents: lazyArray };
            } else {
              var properties = { isDevice: false, url: url };
            }
      
            var node = FS.createFile(parent, name, properties, canRead, canWrite);
            // This is a total hack, but I want to get this lazy file code out of the
            // core of MEMFS. If we want to keep this lazy file concept I feel it should
            // be its own thin LAZYFS proxying calls to MEMFS.
            if (properties.contents) {
              node.contents = properties.contents;
            } else if (properties.url) {
              node.contents = null;
              node.url = properties.url;
            }
            // Add a function that defers querying the file size until it is asked the first time.
            Object.defineProperties(node, {
              usedBytes: {
                get: function() { return this.contents.length; }
              }
            });
            // override each stream op with one that tries to force load the lazy file first
            var stream_ops = {};
            var keys = Object.keys(node.stream_ops);
            keys.forEach((key) => {
              var fn = node.stream_ops[key];
              stream_ops[key] = (...args) => {
                FS.forceLoadFile(node);
                return fn(...args);
              };
            });
            function writeChunks(stream, buffer, offset, length, position) {
              var contents = stream.node.contents;
              if (position >= contents.length)
                return 0;
              var size = Math.min(contents.length - position, length);
              if (contents.slice) { // normal array
                for (var i = 0; i < size; i++) {
                  buffer[offset + i] = contents[position + i];
                }
              } else {
                for (var i = 0; i < size; i++) { // LazyUint8Array from sync binary XHR
                  buffer[offset + i] = contents.get(position + i);
                }
              }
              return size;
            }
            // use a custom read function
            stream_ops.read = (stream, buffer, offset, length, position) => {
              FS.forceLoadFile(node);
              return writeChunks(stream, buffer, offset, length, position)
            };
            // use a custom mmap function
            stream_ops.mmap = (stream, length, position, prot, flags) => {
              FS.forceLoadFile(node);
              var ptr = mmapAlloc(length);
              if (!ptr) {
                throw new FS.ErrnoError(48);
              }
              writeChunks(stream, HEAP8, ptr, length, position);
              return { ptr, allocated: true };
            };
            node.stream_ops = stream_ops;
            return node;
          },
      };
      
      var SYSCALLS = {
      DEFAULT_POLLMASK:5,
      calculateAt(dirfd, path, allowEmpty) {
            if (PATH.isAbs(path)) {
              return path;
            }
            // relative path
            var dir;
            if (dirfd === -100) {
              dir = FS.cwd();
            } else {
              var dirstream = SYSCALLS.getStreamFromFD(dirfd);
              dir = dirstream.path;
            }
            if (path.length == 0) {
              if (!allowEmpty) {
                throw new FS.ErrnoError(44);          }
              return dir;
            }
            return PATH.join2(dir, path);
          },
      doStat(func, path, buf) {
            var stat = func(path);
            HEAP32[((buf)>>2)] = stat.dev;
            HEAP32[(((buf)+(4))>>2)] = stat.mode;
            HEAPU32[(((buf)+(8))>>2)] = stat.nlink;
            HEAP32[(((buf)+(12))>>2)] = stat.uid;
            HEAP32[(((buf)+(16))>>2)] = stat.gid;
            HEAP32[(((buf)+(20))>>2)] = stat.rdev;
            HEAP64[(((buf)+(24))>>3)] = BigInt(stat.size);
            HEAP32[(((buf)+(32))>>2)] = 4096;
            HEAP32[(((buf)+(36))>>2)] = stat.blocks;
            var atime = stat.atime.getTime();
            var mtime = stat.mtime.getTime();
            var ctime = stat.ctime.getTime();
            HEAP64[(((buf)+(40))>>3)] = BigInt(Math.floor(atime / 1000));
            HEAPU32[(((buf)+(48))>>2)] = (atime % 1000) * 1000;
            HEAP64[(((buf)+(56))>>3)] = BigInt(Math.floor(mtime / 1000));
            HEAPU32[(((buf)+(64))>>2)] = (mtime % 1000) * 1000;
            HEAP64[(((buf)+(72))>>3)] = BigInt(Math.floor(ctime / 1000));
            HEAPU32[(((buf)+(80))>>2)] = (ctime % 1000) * 1000;
            HEAP64[(((buf)+(88))>>3)] = BigInt(stat.ino);
            return 0;
          },
      doMsync(addr, stream, len, flags, offset) {
            if (!FS.isFile(stream.node.mode)) {
              throw new FS.ErrnoError(43);
            }
            if (flags & 2) {
              // MAP_PRIVATE calls need not to be synced back to underlying fs
              return 0;
            }
            var buffer = HEAPU8.slice(addr, addr + len);
            FS.msync(stream, buffer, offset, len, flags);
          },
      varargs:undefined,
      get() {
            // the `+` prepended here is necessary to convince the JSCompiler that varargs is indeed a number.
            var ret = HEAP32[((+SYSCALLS.varargs)>>2)];
            SYSCALLS.varargs += 4;
            return ret;
          },
      getp() { return SYSCALLS.get() },
      getStr(ptr) {
            var ret = UTF8ToString(ptr);
            return ret;
          },
      getStreamFromFD(fd) {
            var stream = FS.getStreamChecked(fd);
            return stream;
          },
      };
      function ___syscall_chdir(path) {
      try {
      
          path = SYSCALLS.getStr(path);
          FS.chdir(path);
          return 0;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      function ___syscall_chmod(path, mode) {
      try {
      
          path = SYSCALLS.getStr(path);
          FS.chmod(path, mode);
          return 0;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      var SOCKFS = {
      mount(mount) {
            // If Module['websocket'] has already been defined (e.g. for configuring
            // the subprotocol/url) use that, if not initialise it to a new object.
            Module['websocket'] = (Module['websocket'] &&
                                   ('object' === typeof Module['websocket'])) ? Module['websocket'] : {};
      
            // Add the Event registration mechanism to the exported websocket configuration
            // object so we can register network callbacks from native JavaScript too.
            // For more documentation see system/include/emscripten/emscripten.h
            Module['websocket']._callbacks = {};
            Module['websocket']['on'] = /** @this{Object} */ function(event, callback) {
              if ('function' === typeof callback) {
                this._callbacks[event] = callback;
              }
              return this;
            };
      
            Module['websocket'].emit = /** @this{Object} */ function(event, param) {
              if ('function' === typeof this._callbacks[event]) {
                this._callbacks[event].call(this, param);
              }
            };
      
            // If debug is enabled register simple default logging callbacks for each Event.
      
            return FS.createNode(null, '/', 16384 | 511 /* 0777 */, 0);
          },
      createSocket(family, type, protocol) {
            type &= -526337; // Some applications may pass it; it makes no sense for a single process.
            var streaming = type == 1;
            if (streaming && protocol && protocol != 6) {
              throw new FS.ErrnoError(66); // if SOCK_STREAM, must be tcp or 0.
            }
      
            // create our internal socket structure
            var sock = {
              family,
              type,
              protocol,
              server: null,
              error: null, // Used in getsockopt for SOL_SOCKET/SO_ERROR test
              peers: {},
              pending: [],
              recv_queue: [],
              sock_ops: SOCKFS.websocket_sock_ops
            };
      
            // create the filesystem node to store the socket structure
            var name = SOCKFS.nextname();
            var node = FS.createNode(SOCKFS.root, name, 49152, 0);
            node.sock = sock;
      
            // and the wrapping stream that enables library functions such
            // as read and write to indirectly interact with the socket
            var stream = FS.createStream({
              path: name,
              node,
              flags: 2,
              seekable: false,
              stream_ops: SOCKFS.stream_ops
            });
      
            // map the new stream to the socket structure (sockets have a 1:1
            // relationship with a stream)
            sock.stream = stream;
      
            return sock;
          },
      getSocket(fd) {
            var stream = FS.getStream(fd);
            if (!stream || !FS.isSocket(stream.node.mode)) {
              return null;
            }
            return stream.node.sock;
          },
      stream_ops:{
      poll(stream) {
              var sock = stream.node.sock;
              return sock.sock_ops.poll(sock);
            },
      ioctl(stream, request, varargs) {
              var sock = stream.node.sock;
              return sock.sock_ops.ioctl(sock, request, varargs);
            },
      read(stream, buffer, offset, length, position /* ignored */) {
              var sock = stream.node.sock;
              var msg = sock.sock_ops.recvmsg(sock, length);
              if (!msg) {
                // socket is closed
                return 0;
              }
              buffer.set(msg.buffer, offset);
              return msg.buffer.length;
            },
      write(stream, buffer, offset, length, position /* ignored */) {
              var sock = stream.node.sock;
              return sock.sock_ops.sendmsg(sock, buffer, offset, length);
            },
      close(stream) {
              var sock = stream.node.sock;
              sock.sock_ops.close(sock);
            },
      },
      nextname() {
            if (!SOCKFS.nextname.current) {
              SOCKFS.nextname.current = 0;
            }
            return 'socket[' + (SOCKFS.nextname.current++) + ']';
          },
      websocket_sock_ops:{
      createPeer(sock, addr, port) {
              var ws;
      
              if (typeof addr == 'object') {
                ws = addr;
                addr = null;
                port = null;
              }
      
              if (ws) {
                // for sockets that've already connected (e.g. we're the server)
                // we can inspect the _socket property for the address
                if (ws._socket) {
                  addr = ws._socket.remoteAddress;
                  port = ws._socket.remotePort;
                }
                // if we're just now initializing a connection to the remote,
                // inspect the url property
                else {
                  var result = /ws[s]?:\/\/([^:]+):(\d+)/.exec(ws.url);
                  if (!result) {
                    throw new Error('WebSocket URL must be in the format ws(s)://address:port');
                  }
                  addr = result[1];
                  port = parseInt(result[2], 10);
                }
              } else {
                // create the actual websocket object and connect
                try {
                  // runtimeConfig gets set to true if WebSocket runtime configuration is available.
                  var runtimeConfig = (Module['websocket'] && ('object' === typeof Module['websocket']));
      
                  // The default value is 'ws://' the replace is needed because the compiler replaces '//' comments with '#'
                  // comments without checking context, so we'd end up with ws:#, the replace swaps the '#' for '//' again.
                  var url = 'ws:#'.replace('#', '//');
      
                  if (runtimeConfig) {
                    if ('string' === typeof Module['websocket']['url']) {
                      url = Module['websocket']['url']; // Fetch runtime WebSocket URL config.
                    }
                  }
      
                  if (url === 'ws://' || url === 'wss://') { // Is the supplied URL config just a prefix, if so complete it.
                    var parts = addr.split('/');
                    url = url + parts[0] + ":" + port + "/" + parts.slice(1).join('/');
                  }
      
                  // Make the WebSocket subprotocol (Sec-WebSocket-Protocol) default to binary if no configuration is set.
                  var subProtocols = 'binary'; // The default value is 'binary'
      
                  if (runtimeConfig) {
                    if ('string' === typeof Module['websocket']['subprotocol']) {
                      subProtocols = Module['websocket']['subprotocol']; // Fetch runtime WebSocket subprotocol config.
                    }
                  }
      
                  // The default WebSocket options
                  var opts = undefined;
      
                  if (subProtocols !== 'null') {
                    // The regex trims the string (removes spaces at the beginning and end, then splits the string by
                    // <any space>,<any space> into an Array. Whitespace removal is important for Websockify and ws.
                    subProtocols = subProtocols.replace(/^ +| +$/g,"").split(/ *, */);
      
                    opts = subProtocols;
                  }
      
                  // some webservers (azure) does not support subprotocol header
                  if (runtimeConfig && null === Module['websocket']['subprotocol']) {
                    subProtocols = 'null';
                    opts = undefined;
                  }
      
                  // If node we use the ws library.
                  var WebSocketConstructor;
                  if (ENVIRONMENT_IS_NODE) {
                    WebSocketConstructor = /** @type{(typeof WebSocket)} */(require('ws'));
                  } else
                  {
                    WebSocketConstructor = WebSocket;
                  }
                  ws = new WebSocketConstructor(url, opts);
                  ws.binaryType = 'arraybuffer';
                } catch (e) {
                  throw new FS.ErrnoError(23);
                }
              }
      
              var peer = {
                addr,
                port,
                socket: ws,
                dgram_send_queue: []
              };
      
              SOCKFS.websocket_sock_ops.addPeer(sock, peer);
              SOCKFS.websocket_sock_ops.handlePeerEvents(sock, peer);
      
              // if this is a bound dgram socket, send the port number first to allow
              // us to override the ephemeral port reported to us by remotePort on the
              // remote end.
              if (sock.type === 2 && typeof sock.sport != 'undefined') {
                peer.dgram_send_queue.push(new Uint8Array([
                    255, 255, 255, 255,
                    'p'.charCodeAt(0), 'o'.charCodeAt(0), 'r'.charCodeAt(0), 't'.charCodeAt(0),
                    ((sock.sport & 0xff00) >> 8) , (sock.sport & 0xff)
                ]));
              }
      
              return peer;
            },
      getPeer(sock, addr, port) {
              return sock.peers[addr + ':' + port];
            },
      addPeer(sock, peer) {
              sock.peers[peer.addr + ':' + peer.port] = peer;
            },
      removePeer(sock, peer) {
              delete sock.peers[peer.addr + ':' + peer.port];
            },
      handlePeerEvents(sock, peer) {
              var first = true;
      
              var handleOpen = function () {
      
                Module['websocket'].emit('open', sock.stream.fd);
      
                try {
                  var queued = peer.dgram_send_queue.shift();
                  while (queued) {
                    peer.socket.send(queued);
                    queued = peer.dgram_send_queue.shift();
                  }
                } catch (e) {
                  // not much we can do here in the way of proper error handling as we've already
                  // lied and said this data was sent. shut it down.
                  peer.socket.close();
                }
              };
      
              function handleMessage(data) {
                if (typeof data == 'string') {
                  var encoder = new TextEncoder(); // should be utf-8
                  data = encoder.encode(data); // make a typed array from the string
                } else {
                  assert(data.byteLength !== undefined); // must receive an ArrayBuffer
                  if (data.byteLength == 0) {
                    // An empty ArrayBuffer will emit a pseudo disconnect event
                    // as recv/recvmsg will return zero which indicates that a socket
                    // has performed a shutdown although the connection has not been disconnected yet.
                    return;
                  }
                  data = new Uint8Array(data); // make a typed array view on the array buffer
                }
      
                // if this is the port message, override the peer's port with it
                var wasfirst = first;
                first = false;
                if (wasfirst &&
                    data.length === 10 &&
                    data[0] === 255 && data[1] === 255 && data[2] === 255 && data[3] === 255 &&
                    data[4] === 'p'.charCodeAt(0) && data[5] === 'o'.charCodeAt(0) && data[6] === 'r'.charCodeAt(0) && data[7] === 't'.charCodeAt(0)) {
                  // update the peer's port and it's key in the peer map
                  var newport = ((data[8] << 8) | data[9]);
                  SOCKFS.websocket_sock_ops.removePeer(sock, peer);
                  peer.port = newport;
                  SOCKFS.websocket_sock_ops.addPeer(sock, peer);
                  return;
                }
      
                sock.recv_queue.push({ addr: peer.addr, port: peer.port, data: data });
                Module['websocket'].emit('message', sock.stream.fd);
              }  
              if (ENVIRONMENT_IS_NODE) {
                peer.socket.on('open', handleOpen);
                peer.socket.on('message', function(data, isBinary) {
                  if (!isBinary) {
                    return;
                  }
                  handleMessage((new Uint8Array(data)).buffer); // copy from node Buffer -> ArrayBuffer
                });
                peer.socket.on('close', function() {
                  Module['websocket'].emit('close', sock.stream.fd);
                });
                peer.socket.on('error', function(error) {
                  // Although the ws library may pass errors that may be more descriptive than
                  // ECONNREFUSED they are not necessarily the expected error code e.g.
                  // ENOTFOUND on getaddrinfo seems to be node.js specific, so using ECONNREFUSED
                  // is still probably the most useful thing to do.
                  sock.error = 14; // Used in getsockopt for SOL_SOCKET/SO_ERROR test.
                  Module['websocket'].emit('error', [sock.stream.fd, sock.error, 'ECONNREFUSED: Connection refused']);
                  // don't throw
                });
              } else {
                peer.socket.onopen = handleOpen;
                peer.socket.onclose = function() {
                  Module['websocket'].emit('close', sock.stream.fd);
                };
                peer.socket.onmessage = function peer_socket_onmessage(event) {
                  handleMessage(event.data);
                };
                peer.socket.onerror = function(error) {
                  // The WebSocket spec only allows a 'simple event' to be thrown on error,
                  // so we only really know as much as ECONNREFUSED.
                  sock.error = 14; // Used in getsockopt for SOL_SOCKET/SO_ERROR test.
                  Module['websocket'].emit('error', [sock.stream.fd, sock.error, 'ECONNREFUSED: Connection refused']);
                };
              }
            },
      poll(sock) {
              if (sock.type === 1 && sock.server) {
                // listen sockets should only say they're available for reading
                // if there are pending clients.
                return sock.pending.length ? (64 | 1) : 0;
              }
      
              var mask = 0;
              var dest = sock.type === 1 ?  // we only care about the socket state for connection-based sockets
                SOCKFS.websocket_sock_ops.getPeer(sock, sock.daddr, sock.dport) :
                null;
      
              if (sock.recv_queue.length ||
                  !dest ||  // connection-less sockets are always ready to read
                  (dest && dest.socket.readyState === dest.socket.CLOSING) ||
                  (dest && dest.socket.readyState === dest.socket.CLOSED)) {  // let recv return 0 once closed
                mask |= (64 | 1);
              }
      
              if (!dest ||  // connection-less sockets are always ready to write
                  (dest && dest.socket.readyState === dest.socket.OPEN)) {
                mask |= 4;
              }
      
              if ((dest && dest.socket.readyState === dest.socket.CLOSING) ||
                  (dest && dest.socket.readyState === dest.socket.CLOSED)) {
                mask |= 16;
              }
      
              return mask;
            },
      ioctl(sock, request, arg) {
              switch (request) {
                case 21531:
                  var bytes = 0;
                  if (sock.recv_queue.length) {
                    bytes = sock.recv_queue[0].data.length;
                  }
                  HEAP32[((arg)>>2)] = bytes;
                  return 0;
                default:
                  return 28;
              }
            },
      close(sock) {
              // if we've spawned a listen server, close it
              if (sock.server) {
                try {
                  sock.server.close();
                } catch (e) {
                }
                sock.server = null;
              }
              // close any peer connections
              var peers = Object.keys(sock.peers);
              for (var i = 0; i < peers.length; i++) {
                var peer = sock.peers[peers[i]];
                try {
                  peer.socket.close();
                } catch (e) {
                }
                SOCKFS.websocket_sock_ops.removePeer(sock, peer);
              }
              return 0;
            },
      bind(sock, addr, port) {
              if (typeof sock.saddr != 'undefined' || typeof sock.sport != 'undefined') {
                throw new FS.ErrnoError(28);  // already bound
              }
              sock.saddr = addr;
              sock.sport = port;
              // in order to emulate dgram sockets, we need to launch a listen server when
              // binding on a connection-less socket
              // note: this is only required on the server side
              if (sock.type === 2) {
                // close the existing server if it exists
                if (sock.server) {
                  sock.server.close();
                  sock.server = null;
                }
                // swallow error operation not supported error that occurs when binding in the
                // browser where this isn't supported
                try {
                  sock.sock_ops.listen(sock, 0);
                } catch (e) {
                  if (!(e.name === 'ErrnoError')) throw e;
                  if (e.errno !== 138) throw e;
                }
              }
            },
      connect(sock, addr, port) {
              if (sock.server) {
                throw new FS.ErrnoError(138);
              }
      
              // TODO autobind
              // if (!sock.addr && sock.type == 2) {
              // }
      
              // early out if we're already connected / in the middle of connecting
              if (typeof sock.daddr != 'undefined' && typeof sock.dport != 'undefined') {
                var dest = SOCKFS.websocket_sock_ops.getPeer(sock, sock.daddr, sock.dport);
                if (dest) {
                  if (dest.socket.readyState === dest.socket.CONNECTING) {
                    throw new FS.ErrnoError(7);
                  } else {
                    throw new FS.ErrnoError(30);
                  }
                }
              }
      
              // add the socket to our peer list and set our
              // destination address / port to match
              var peer = SOCKFS.websocket_sock_ops.createPeer(sock, addr, port);
              sock.daddr = peer.addr;
              sock.dport = peer.port;
      
              // always "fail" in non-blocking mode
              throw new FS.ErrnoError(26);
            },
      listen(sock, backlog) {
              if (!ENVIRONMENT_IS_NODE) {
                throw new FS.ErrnoError(138);
              }
              if (sock.server) {
                 throw new FS.ErrnoError(28);  // already listening
              }
              var WebSocketServer = require('ws').Server;
              var host = sock.saddr;
              sock.server = new WebSocketServer({
                host,
                port: sock.sport
                // TODO support backlog
              });
              Module['websocket'].emit('listen', sock.stream.fd); // Send Event with listen fd.
      
              sock.server.on('connection', function(ws) {
                if (sock.type === 1) {
                  var newsock = SOCKFS.createSocket(sock.family, sock.type, sock.protocol);
      
                  // create a peer on the new socket
                  var peer = SOCKFS.websocket_sock_ops.createPeer(newsock, ws);
                  newsock.daddr = peer.addr;
                  newsock.dport = peer.port;
      
                  // push to queue for accept to pick up
                  sock.pending.push(newsock);
                  Module['websocket'].emit('connection', newsock.stream.fd);
                } else {
                  // create a peer on the listen socket so calling sendto
                  // with the listen socket and an address will resolve
                  // to the correct client
                  SOCKFS.websocket_sock_ops.createPeer(sock, ws);
                  Module['websocket'].emit('connection', sock.stream.fd);
                }
              });
              sock.server.on('close', function() {
                Module['websocket'].emit('close', sock.stream.fd);
                sock.server = null;
              });
              sock.server.on('error', function(error) {
                // Although the ws library may pass errors that may be more descriptive than
                // ECONNREFUSED they are not necessarily the expected error code e.g.
                // ENOTFOUND on getaddrinfo seems to be node.js specific, so using EHOSTUNREACH
                // is still probably the most useful thing to do. This error shouldn't
                // occur in a well written app as errors should get trapped in the compiled
                // app's own getaddrinfo call.
                sock.error = 23; // Used in getsockopt for SOL_SOCKET/SO_ERROR test.
                Module['websocket'].emit('error', [sock.stream.fd, sock.error, 'EHOSTUNREACH: Host is unreachable']);
                // don't throw
              });
            },
      accept(listensock) {
              if (!listensock.server || !listensock.pending.length) {
                throw new FS.ErrnoError(28);
              }
              var newsock = listensock.pending.shift();
              newsock.stream.flags = listensock.stream.flags;
              return newsock;
            },
      getname(sock, peer) {
              var addr, port;
              if (peer) {
                if (sock.daddr === undefined || sock.dport === undefined) {
                  throw new FS.ErrnoError(53);
                }
                addr = sock.daddr;
                port = sock.dport;
              } else {
                // TODO saddr and sport will be set for bind()'d UDP sockets, but what
                // should we be returning for TCP sockets that've been connect()'d?
                addr = sock.saddr || 0;
                port = sock.sport || 0;
              }
              return { addr, port };
            },
      sendmsg(sock, buffer, offset, length, addr, port) {
              if (sock.type === 2) {
                // connection-less sockets will honor the message address,
                // and otherwise fall back to the bound destination address
                if (addr === undefined || port === undefined) {
                  addr = sock.daddr;
                  port = sock.dport;
                }
                // if there was no address to fall back to, error out
                if (addr === undefined || port === undefined) {
                  throw new FS.ErrnoError(17);
                }
              } else {
                // connection-based sockets will only use the bound
                addr = sock.daddr;
                port = sock.dport;
              }
      
              // find the peer for the destination address
              var dest = SOCKFS.websocket_sock_ops.getPeer(sock, addr, port);
      
              // early out if not connected with a connection-based socket
              if (sock.type === 1) {
                if (!dest || dest.socket.readyState === dest.socket.CLOSING || dest.socket.readyState === dest.socket.CLOSED) {
                  throw new FS.ErrnoError(53);
                } else if (dest.socket.readyState === dest.socket.CONNECTING) {
                  throw new FS.ErrnoError(6);
                }
              }
      
              // create a copy of the incoming data to send, as the WebSocket API
              // doesn't work entirely with an ArrayBufferView, it'll just send
              // the entire underlying buffer
              if (ArrayBuffer.isView(buffer)) {
                offset += buffer.byteOffset;
                buffer = buffer.buffer;
              }
      
              var data;
                data = buffer.slice(offset, offset + length);
      
              // if we're emulating a connection-less dgram socket and don't have
              // a cached connection, queue the buffer to send upon connect and
              // lie, saying the data was sent now.
              if (sock.type === 2) {
                if (!dest || dest.socket.readyState !== dest.socket.OPEN) {
                  // if we're not connected, open a new connection
                  if (!dest || dest.socket.readyState === dest.socket.CLOSING || dest.socket.readyState === dest.socket.CLOSED) {
                    dest = SOCKFS.websocket_sock_ops.createPeer(sock, addr, port);
                  }
                  dest.dgram_send_queue.push(data);
                  return length;
                }
              }
      
              try {
                // send the actual data
                dest.socket.send(data);
                return length;
              } catch (e) {
                throw new FS.ErrnoError(28);
              }
            },
      recvmsg(sock, length) {
              // http://pubs.opengroup.org/onlinepubs/7908799/xns/recvmsg.html
              if (sock.type === 1 && sock.server) {
                // tcp servers should not be recv()'ing on the listen socket
                throw new FS.ErrnoError(53);
              }
      
              var queued = sock.recv_queue.shift();
              if (!queued) {
                if (sock.type === 1) {
                  var dest = SOCKFS.websocket_sock_ops.getPeer(sock, sock.daddr, sock.dport);
      
                  if (!dest) {
                    // if we have a destination address but are not connected, error out
                    throw new FS.ErrnoError(53);
                  }
                  if (dest.socket.readyState === dest.socket.CLOSING || dest.socket.readyState === dest.socket.CLOSED) {
                    // return null if the socket has closed
                    return null;
                  }
                  // else, our socket is in a valid state but truly has nothing available
                  throw new FS.ErrnoError(6);
                }
                throw new FS.ErrnoError(6);
              }
      
              // queued.data will be an ArrayBuffer if it's unadulterated, but if it's
              // requeued TCP data it'll be an ArrayBufferView
              var queuedLength = queued.data.byteLength || queued.data.length;
              var queuedOffset = queued.data.byteOffset || 0;
              var queuedBuffer = queued.data.buffer || queued.data;
              var bytesRead = Math.min(length, queuedLength);
              var res = {
                buffer: new Uint8Array(queuedBuffer, queuedOffset, bytesRead),
                addr: queued.addr,
                port: queued.port
              };
      
              // push back any unread data for TCP connections
              if (sock.type === 1 && bytesRead < queuedLength) {
                var bytesRemaining = queuedLength - bytesRead;
                queued.data = new Uint8Array(queuedBuffer, queuedOffset + bytesRead, bytesRemaining);
                sock.recv_queue.unshift(queued);
              }
      
              return res;
            },
      },
      };
      
      var getSocketFromFD = (fd) => {
          var socket = SOCKFS.getSocket(fd);
          if (!socket) throw new FS.ErrnoError(8);
          return socket;
        };
      
      var inetNtop4 = (addr) => {
          return (addr & 0xff) + '.' + ((addr >> 8) & 0xff) + '.' + ((addr >> 16) & 0xff) + '.' + ((addr >> 24) & 0xff)
        };
      
      
      var inetNtop6 = (ints) => {
          //  ref:  http://www.ietf.org/rfc/rfc2373.txt - section 2.5.4
          //  Format for IPv4 compatible and mapped  128-bit IPv6 Addresses
          //  128-bits are split into eight 16-bit words
          //  stored in network byte order (big-endian)
          //  |                80 bits               | 16 |      32 bits        |
          //  +-----------------------------------------------------------------+
          //  |               10 bytes               |  2 |      4 bytes        |
          //  +--------------------------------------+--------------------------+
          //  +               5 words                |  1 |      2 words        |
          //  +--------------------------------------+--------------------------+
          //  |0000..............................0000|0000|    IPv4 ADDRESS     | (compatible)
          //  +--------------------------------------+----+---------------------+
          //  |0000..............................0000|FFFF|    IPv4 ADDRESS     | (mapped)
          //  +--------------------------------------+----+---------------------+
          var str = "";
          var word = 0;
          var longest = 0;
          var lastzero = 0;
          var zstart = 0;
          var len = 0;
          var i = 0;
          var parts = [
            ints[0] & 0xffff,
            (ints[0] >> 16),
            ints[1] & 0xffff,
            (ints[1] >> 16),
            ints[2] & 0xffff,
            (ints[2] >> 16),
            ints[3] & 0xffff,
            (ints[3] >> 16)
          ];
      
          // Handle IPv4-compatible, IPv4-mapped, loopback and any/unspecified addresses
      
          var hasipv4 = true;
          var v4part = "";
          // check if the 10 high-order bytes are all zeros (first 5 words)
          for (i = 0; i < 5; i++) {
            if (parts[i] !== 0) { hasipv4 = false; break; }
          }
      
          if (hasipv4) {
            // low-order 32-bits store an IPv4 address (bytes 13 to 16) (last 2 words)
            v4part = inetNtop4(parts[6] | (parts[7] << 16));
            // IPv4-mapped IPv6 address if 16-bit value (bytes 11 and 12) == 0xFFFF (6th word)
            if (parts[5] === -1) {
              str = "::ffff:";
              str += v4part;
              return str;
            }
            // IPv4-compatible IPv6 address if 16-bit value (bytes 11 and 12) == 0x0000 (6th word)
            if (parts[5] === 0) {
              str = "::";
              //special case IPv6 addresses
              if (v4part === "0.0.0.0") v4part = ""; // any/unspecified address
              if (v4part === "0.0.0.1") v4part = "1";// loopback address
              str += v4part;
              return str;
            }
          }
      
          // Handle all other IPv6 addresses
      
          // first run to find the longest contiguous zero words
          for (word = 0; word < 8; word++) {
            if (parts[word] === 0) {
              if (word - lastzero > 1) {
                len = 0;
              }
              lastzero = word;
              len++;
            }
            if (len > longest) {
              longest = len;
              zstart = word - longest + 1;
            }
          }
      
          for (word = 0; word < 8; word++) {
            if (longest > 1) {
              // compress contiguous zeros - to produce "::"
              if (parts[word] === 0 && word >= zstart && word < (zstart + longest) ) {
                if (word === zstart) {
                  str += ":";
                  if (zstart === 0) str += ":"; //leading zeros case
                }
                continue;
              }
            }
            // converts 16-bit words from big-endian to little-endian before converting to hex string
            str += Number(_ntohs(parts[word] & 0xffff)).toString(16);
            str += word < 7 ? ":" : "";
          }
          return str;
        };
      
      var readSockaddr = (sa, salen) => {
          // family / port offsets are common to both sockaddr_in and sockaddr_in6
          var family = HEAP16[((sa)>>1)];
          var port = _ntohs(HEAPU16[(((sa)+(2))>>1)]);
          var addr;
      
          switch (family) {
            case 2:
              if (salen !== 16) {
                return { errno: 28 };
              }
              addr = HEAP32[(((sa)+(4))>>2)];
              addr = inetNtop4(addr);
              break;
            case 10:
              if (salen !== 28) {
                return { errno: 28 };
              }
              addr = [
                HEAP32[(((sa)+(8))>>2)],
                HEAP32[(((sa)+(12))>>2)],
                HEAP32[(((sa)+(16))>>2)],
                HEAP32[(((sa)+(20))>>2)]
              ];
              addr = inetNtop6(addr);
              break;
            default:
              return { errno: 5 };
          }
      
          return { family: family, addr: addr, port: port };
        };
      
      
      var inetPton4 = (str) => {
          var b = str.split('.');
          for (var i = 0; i < 4; i++) {
            var tmp = Number(b[i]);
            if (isNaN(tmp)) return null;
            b[i] = tmp;
          }
          return (b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24)) >>> 0;
        };
      
      
      /** @suppress {checkTypes} */
      var jstoi_q = (str) => parseInt(str);
      var inetPton6 = (str) => {
          var words;
          var w, offset, z;
          /* http://home.deds.nl/~aeron/regex/ */
          var valid6regx = /^((?=.*::)(?!.*::.+::)(::)?([\dA-F]{1,4}:(:|\b)|){5}|([\dA-F]{1,4}:){6})((([\dA-F]{1,4}((?!\3)::|:\b|$))|(?!\2\3)){2}|(((2[0-4]|1\d|[1-9])?\d|25[0-5])\.?\b){4})$/i;
          var parts = [];
          if (!valid6regx.test(str)) {
            return null;
          }
          if (str === "::") {
            return [0, 0, 0, 0, 0, 0, 0, 0];
          }
          // Z placeholder to keep track of zeros when splitting the string on ":"
          if (str.startsWith("::")) {
            str = str.replace("::", "Z:"); // leading zeros case
          } else {
            str = str.replace("::", ":Z:");
          }
      
          if (str.indexOf(".") > 0) {
            // parse IPv4 embedded stress
            str = str.replace(new RegExp('[.]', 'g'), ":");
            words = str.split(":");
            words[words.length-4] = jstoi_q(words[words.length-4]) + jstoi_q(words[words.length-3])*256;
            words[words.length-3] = jstoi_q(words[words.length-2]) + jstoi_q(words[words.length-1])*256;
            words = words.slice(0, words.length-2);
          } else {
            words = str.split(":");
          }
      
          offset = 0; z = 0;
          for (w=0; w < words.length; w++) {
            if (typeof words[w] == 'string') {
              if (words[w] === 'Z') {
                // compressed zeros - write appropriate number of zero words
                for (z = 0; z < (8 - words.length+1); z++) {
                  parts[w+z] = 0;
                }
                offset = z-1;
              } else {
                // parse hex to field to 16-bit value and write it in network byte-order
                parts[w+offset] = _htons(parseInt(words[w],16));
              }
            } else {
              // parsed IPv4 words
              parts[w+offset] = words[w];
            }
          }
          return [
            (parts[1] << 16) | parts[0],
            (parts[3] << 16) | parts[2],
            (parts[5] << 16) | parts[4],
            (parts[7] << 16) | parts[6]
          ];
        };
      var DNS = {
      address_map:{
      id:1,
      addrs:{
      },
      names:{
      },
      },
      lookup_name(name) {
            // If the name is already a valid ipv4 / ipv6 address, don't generate a fake one.
            var res = inetPton4(name);
            if (res !== null) {
              return name;
            }
            res = inetPton6(name);
            if (res !== null) {
              return name;
            }
      
            // See if this name is already mapped.
            var addr;
      
            if (DNS.address_map.addrs[name]) {
              addr = DNS.address_map.addrs[name];
            } else {
              var id = DNS.address_map.id++;
              assert(id < 65535, 'exceeded max address mappings of 65535');
      
              addr = '172.29.' + (id & 0xff) + '.' + (id & 0xff00);
      
              DNS.address_map.names[addr] = name;
              DNS.address_map.addrs[name] = addr;
            }
      
            return addr;
          },
      lookup_addr(addr) {
            if (DNS.address_map.names[addr]) {
              return DNS.address_map.names[addr];
            }
      
            return null;
          },
      };
      /** @param {boolean=} allowNull */
      var getSocketAddress = (addrp, addrlen, allowNull) => {
          if (allowNull && addrp === 0) return null;
          var info = readSockaddr(addrp, addrlen);
          if (info.errno) throw new FS.ErrnoError(info.errno);
          info.addr = DNS.lookup_addr(info.addr) || info.addr;
          return info;
        };
      function ___syscall_connect(fd, addr, addrlen, d1, d2, d3) {
      try {
      
          var sock = getSocketFromFD(fd);
          var info = getSocketAddress(addr, addrlen);
          sock.sock_ops.connect(sock, info.addr, info.port);
          return 0;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      function ___syscall_faccessat(dirfd, path, amode, flags) {
      try {
      
          path = SYSCALLS.getStr(path);
          path = SYSCALLS.calculateAt(dirfd, path);
          if (amode & ~7) {
            // need a valid mode
            return -28;
          }
          var lookup = FS.lookupPath(path, { follow: true });
          var node = lookup.node;
          if (!node) {
            return -44;
          }
          var perms = '';
          if (amode & 4) perms += 'r';
          if (amode & 2) perms += 'w';
          if (amode & 1) perms += 'x';
          if (perms /* otherwise, they've just passed F_OK */ && FS.nodePermissions(node, perms)) {
            return -2;
          }
          return 0;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      var ___syscall_fadvise64 = (fd, offset, len, advice) => {
          return 0; // your advice is important to us (but we can't use it)
        };

      function ___syscall_fchmod(fd, mode) {
      try {
      
          FS.fchmod(fd, mode);
          return 0;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      function ___syscall_fcntl64(fd, cmd, varargs) {
      SYSCALLS.varargs = varargs;
      try {
      
          var stream = SYSCALLS.getStreamFromFD(fd);
          switch (cmd) {
            case 0: {
              var arg = SYSCALLS.get();
              if (arg < 0) {
                return -28;
              }
              while (FS.streams[arg]) {
                arg++;
              }
              var newStream;
              newStream = FS.dupStream(stream, arg);
              return newStream.fd;
            }
            case 1:
            case 2:
              return 0;  // FD_CLOEXEC makes no sense for a single process.
            case 3:
              return stream.flags;
            case 4: {
              var arg = SYSCALLS.get();
              stream.flags |= arg;
              return 0;
            }
            case 12: {
              var arg = SYSCALLS.getp();
              var offset = 0;
              // We're always unlocked.
              HEAP16[(((arg)+(offset))>>1)] = 2;
              return 0;
            }
            case 13:
            case 14:
              return 0; // Pretend that the locking is successful.
          }
          return -28;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      function ___syscall_fstat64(fd, buf) {
      try {
      
          var stream = SYSCALLS.getStreamFromFD(fd);
          return SYSCALLS.doStat(FS.stat, stream.path, buf);
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      function ___syscall_statfs64(path, size, buf) {
      try {
      
          path = SYSCALLS.getStr(path);
          // NOTE: None of the constants here are true. We're just returning safe and
          //       sane values.
          HEAP32[(((buf)+(4))>>2)] = 4096;
          HEAP32[(((buf)+(40))>>2)] = 4096;
          HEAP32[(((buf)+(8))>>2)] = 1000000;
          HEAP32[(((buf)+(12))>>2)] = 500000;
          HEAP32[(((buf)+(16))>>2)] = 500000;
          HEAP32[(((buf)+(20))>>2)] = FS.nextInode;
          HEAP32[(((buf)+(24))>>2)] = 1000000;
          HEAP32[(((buf)+(28))>>2)] = 42;
          HEAP32[(((buf)+(44))>>2)] = 2;  // ST_NOSUID
          HEAP32[(((buf)+(36))>>2)] = 255;
          return 0;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }
      
      function ___syscall_fstatfs64(fd, size, buf) {
      try {
      
          var stream = SYSCALLS.getStreamFromFD(fd);
          return ___syscall_statfs64(0, size, buf);
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      var MAX_INT53 = 9007199254740992;
      
      var MIN_INT53 = -9007199254740992;
      var bigintToI53Checked = (num) => (num < MIN_INT53 || num > MAX_INT53) ? NaN : Number(num);
      function ___syscall_ftruncate64(fd, length) {
        length = bigintToI53Checked(length);
      
        
      try {
      
          if (isNaN(length)) return 61;
          FS.ftruncate(fd, length);
          return 0;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      
      var stringToUTF8 = (str, outPtr, maxBytesToWrite) => {
          return stringToUTF8Array(str, HEAPU8, outPtr, maxBytesToWrite);
        };
      function ___syscall_getcwd(buf, size) {
      try {
      
          if (size === 0) return -28;
          var cwd = FS.cwd();
          var cwdLengthInBytes = lengthBytesUTF8(cwd) + 1;
          if (size < cwdLengthInBytes) return -68;
          stringToUTF8(cwd, buf, size);
          return cwdLengthInBytes;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      
      function ___syscall_getdents64(fd, dirp, count) {
      try {
      
          var stream = SYSCALLS.getStreamFromFD(fd);
          stream.getdents ||= FS.readdir(stream.path);
      
          var struct_size = 280;
          var pos = 0;
          var off = FS.llseek(stream, 0, 1);
      
          var idx = Math.floor(off / struct_size);
      
          while (idx < stream.getdents.length && pos + struct_size <= count) {
            var id;
            var type;
            var name = stream.getdents[idx];
            if (name === '.') {
              id = stream.node.id;
              type = 4; // DT_DIR
            }
            else if (name === '..') {
              var lookup = FS.lookupPath(stream.path, { parent: true });
              id = lookup.node.id;
              type = 4; // DT_DIR
            }
            else {
              var child = FS.lookupNode(stream.node, name);
              id = child.id;
              type = FS.isChrdev(child.mode) ? 2 :  // DT_CHR, character device.
                     FS.isDir(child.mode) ? 4 :     // DT_DIR, directory.
                     FS.isLink(child.mode) ? 10 :   // DT_LNK, symbolic link.
                     8;                             // DT_REG, regular file.
            }
            HEAP64[((dirp + pos)>>3)] = BigInt(id);
            HEAP64[(((dirp + pos)+(8))>>3)] = BigInt((idx + 1) * struct_size);
            HEAP16[(((dirp + pos)+(16))>>1)] = 280;
            HEAP8[(dirp + pos)+(18)] = type;
            stringToUTF8(name, dirp + pos + 19, 256);
            pos += struct_size;
            idx += 1;
          }
          FS.llseek(stream, idx * struct_size, 0);
          return pos;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      function ___syscall_ioctl(fd, op, varargs) {
      SYSCALLS.varargs = varargs;
      try {
      
          var stream = SYSCALLS.getStreamFromFD(fd);
          switch (op) {
            case 21509: {
              if (!stream.tty) return -59;
              return 0;
            }
            case 21505: {
              if (!stream.tty) return -59;
              if (stream.tty.ops.ioctl_tcgets) {
                var termios = stream.tty.ops.ioctl_tcgets(stream);
                var argp = SYSCALLS.getp();
                HEAP32[((argp)>>2)] = termios.c_iflag || 0;
                HEAP32[(((argp)+(4))>>2)] = termios.c_oflag || 0;
                HEAP32[(((argp)+(8))>>2)] = termios.c_cflag || 0;
                HEAP32[(((argp)+(12))>>2)] = termios.c_lflag || 0;
                for (var i = 0; i < 32; i++) {
                  HEAP8[(argp + i)+(17)] = termios.c_cc[i] || 0;
                }
                return 0;
              }
              return 0;
            }
            case 21510:
            case 21511:
            case 21512: {
              if (!stream.tty) return -59;
              return 0; // no-op, not actually adjusting terminal settings
            }
            case 21506:
            case 21507:
            case 21508: {
              if (!stream.tty) return -59;
              if (stream.tty.ops.ioctl_tcsets) {
                var argp = SYSCALLS.getp();
                var c_iflag = HEAP32[((argp)>>2)];
                var c_oflag = HEAP32[(((argp)+(4))>>2)];
                var c_cflag = HEAP32[(((argp)+(8))>>2)];
                var c_lflag = HEAP32[(((argp)+(12))>>2)];
                var c_cc = [];
                for (var i = 0; i < 32; i++) {
                  c_cc.push(HEAP8[(argp + i)+(17)]);
                }
                return stream.tty.ops.ioctl_tcsets(stream.tty, op, { c_iflag, c_oflag, c_cflag, c_lflag, c_cc });
              }
              return 0; // no-op, not actually adjusting terminal settings
            }
            case 21519: {
              if (!stream.tty) return -59;
              var argp = SYSCALLS.getp();
              HEAP32[((argp)>>2)] = 0;
              return 0;
            }
            case 21520: {
              if (!stream.tty) return -59;
              return -28; // not supported
            }
            case 21531: {
              var argp = SYSCALLS.getp();
              return FS.ioctl(stream, op, argp);
            }
            case 21523: {
              // TODO: in theory we should write to the winsize struct that gets
              // passed in, but for now musl doesn't read anything on it
              if (!stream.tty) return -59;
              if (stream.tty.ops.ioctl_tiocgwinsz) {
                var winsize = stream.tty.ops.ioctl_tiocgwinsz(stream.tty);
                var argp = SYSCALLS.getp();
                HEAP16[((argp)>>1)] = winsize[0];
                HEAP16[(((argp)+(2))>>1)] = winsize[1];
              }
              return 0;
            }
            case 21524: {
              // TODO: technically, this ioctl call should change the window size.
              // but, since emscripten doesn't have any concept of a terminal window
              // yet, we'll just silently throw it away as we do TIOCGWINSZ
              if (!stream.tty) return -59;
              return 0;
            }
            case 21515: {
              if (!stream.tty) return -59;
              return 0;
            }
            default: return -28; // not supported
          }
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      function ___syscall_lstat64(path, buf) {
      try {
      
          path = SYSCALLS.getStr(path);
          return SYSCALLS.doStat(FS.lstat, path, buf);
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      function ___syscall_mkdirat(dirfd, path, mode) {
      try {
      
          path = SYSCALLS.getStr(path);
          path = SYSCALLS.calculateAt(dirfd, path);
          // remove a trailing slash, if one - /a/b/ has basename of '', but
          // we want to create b in the context of this function
          path = PATH.normalize(path);
          if (path[path.length-1] === '/') path = path.substr(0, path.length-1);
          FS.mkdir(path, mode, 0);
          return 0;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      function ___syscall_newfstatat(dirfd, path, buf, flags) {
      try {
      
          path = SYSCALLS.getStr(path);
          var nofollow = flags & 256;
          var allowEmpty = flags & 4096;
          flags = flags & (~6400);
          path = SYSCALLS.calculateAt(dirfd, path, allowEmpty);
          return SYSCALLS.doStat(nofollow ? FS.lstat : FS.stat, path, buf);
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      function ___syscall_openat(dirfd, path, flags, varargs) {
      SYSCALLS.varargs = varargs;
      try {
      
          path = SYSCALLS.getStr(path);
          path = SYSCALLS.calculateAt(dirfd, path);
          var mode = varargs ? SYSCALLS.get() : 0;
          return FS.open(path, flags, mode).fd;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      
      
      function ___syscall_readlinkat(dirfd, path, buf, bufsize) {
      try {
      
          path = SYSCALLS.getStr(path);
          path = SYSCALLS.calculateAt(dirfd, path);
          if (bufsize <= 0) return -28;
          var ret = FS.readlink(path);
      
          var len = Math.min(bufsize, lengthBytesUTF8(ret));
          var endChar = HEAP8[buf+len];
          stringToUTF8(ret, buf, bufsize+1);
          // readlink is one of the rare functions that write out a C string, but does never append a null to the output buffer(!)
          // stringToUTF8() always appends a null byte, so restore the character under the null byte after the write.
          HEAP8[buf+len] = endChar;
          return len;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      function ___syscall_renameat(olddirfd, oldpath, newdirfd, newpath) {
      try {
      
          oldpath = SYSCALLS.getStr(oldpath);
          newpath = SYSCALLS.getStr(newpath);
          oldpath = SYSCALLS.calculateAt(olddirfd, oldpath);
          newpath = SYSCALLS.calculateAt(newdirfd, newpath);
          FS.rename(oldpath, newpath);
          return 0;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      function ___syscall_rmdir(path) {
      try {
      
          path = SYSCALLS.getStr(path);
          FS.rmdir(path);
          return 0;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      
      function ___syscall_sendto(fd, message, length, flags, addr, addr_len) {
      try {
      
          var sock = getSocketFromFD(fd);
          var dest = getSocketAddress(addr, addr_len, true);
          if (!dest) {
            // send, no address provided
            return FS.write(sock.stream, HEAP8, message, length);
          }
          // sendto an address
          return sock.sock_ops.sendmsg(sock, HEAP8, message, length, dest.addr, dest.port);
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      function ___syscall_socket(domain, type, protocol) {
      try {
      
          var sock = SOCKFS.createSocket(domain, type, protocol);
          return sock.stream.fd;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      function ___syscall_stat64(path, buf) {
      try {
      
          path = SYSCALLS.getStr(path);
          return SYSCALLS.doStat(FS.stat, path, buf);
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      function ___syscall_symlink(target, linkpath) {
      try {
      
          target = SYSCALLS.getStr(target);
          linkpath = SYSCALLS.getStr(linkpath);
          FS.symlink(target, linkpath);
          return 0;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      function ___syscall_unlinkat(dirfd, path, flags) {
      try {
      
          path = SYSCALLS.getStr(path);
          path = SYSCALLS.calculateAt(dirfd, path);
          if (flags === 0) {
            FS.unlink(path);
          } else if (flags === 512) {
            FS.rmdir(path);
          } else {
            abort('Invalid flags passed to unlinkat');
          }
          return 0;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      var readI53FromI64 = (ptr) => {
          return HEAPU32[((ptr)>>2)] + HEAP32[(((ptr)+(4))>>2)] * 4294967296;
        };
      
      function ___syscall_utimensat(dirfd, path, times, flags) {
      try {
      
          path = SYSCALLS.getStr(path);
          path = SYSCALLS.calculateAt(dirfd, path, true);
          if (!times) {
            var atime = Date.now();
            var mtime = atime;
          } else {
            var seconds = readI53FromI64(times);
            var nanoseconds = HEAP32[(((times)+(8))>>2)];
            atime = (seconds*1000) + (nanoseconds/(1000*1000));
            times += 16;
            seconds = readI53FromI64(times);
            nanoseconds = HEAP32[(((times)+(8))>>2)];
            mtime = (seconds*1000) + (nanoseconds/(1000*1000));
          }
          FS.utime(path, atime, mtime);
          return 0;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      var nowIsMonotonic = 1;
      var __emscripten_get_now_is_monotonic = () => nowIsMonotonic;

      function __gmtime_js(time, tmPtr) {
        time = bigintToI53Checked(time);
      
        
          var date = new Date(time * 1000);
          HEAP32[((tmPtr)>>2)] = date.getUTCSeconds();
          HEAP32[(((tmPtr)+(4))>>2)] = date.getUTCMinutes();
          HEAP32[(((tmPtr)+(8))>>2)] = date.getUTCHours();
          HEAP32[(((tmPtr)+(12))>>2)] = date.getUTCDate();
          HEAP32[(((tmPtr)+(16))>>2)] = date.getUTCMonth();
          HEAP32[(((tmPtr)+(20))>>2)] = date.getUTCFullYear()-1900;
          HEAP32[(((tmPtr)+(24))>>2)] = date.getUTCDay();
          var start = Date.UTC(date.getUTCFullYear(), 0, 1, 0, 0, 0, 0);
          var yday = ((date.getTime() - start) / (1000 * 60 * 60 * 24))|0;
          HEAP32[(((tmPtr)+(28))>>2)] = yday;
      }

      var isLeapYear = (year) => year%4 === 0 && (year%100 !== 0 || year%400 === 0);
      
      var MONTH_DAYS_LEAP_CUMULATIVE = [0,31,60,91,121,152,182,213,244,274,305,335];
      
      var MONTH_DAYS_REGULAR_CUMULATIVE = [0,31,59,90,120,151,181,212,243,273,304,334];
      var ydayFromDate = (date) => {
          var leap = isLeapYear(date.getFullYear());
          var monthDaysCumulative = (leap ? MONTH_DAYS_LEAP_CUMULATIVE : MONTH_DAYS_REGULAR_CUMULATIVE);
          var yday = monthDaysCumulative[date.getMonth()] + date.getDate() - 1; // -1 since it's days since Jan 1
      
          return yday;
        };
      
      function __localtime_js(time, tmPtr) {
        time = bigintToI53Checked(time);
      
        
          var date = new Date(time*1000);
          HEAP32[((tmPtr)>>2)] = date.getSeconds();
          HEAP32[(((tmPtr)+(4))>>2)] = date.getMinutes();
          HEAP32[(((tmPtr)+(8))>>2)] = date.getHours();
          HEAP32[(((tmPtr)+(12))>>2)] = date.getDate();
          HEAP32[(((tmPtr)+(16))>>2)] = date.getMonth();
          HEAP32[(((tmPtr)+(20))>>2)] = date.getFullYear()-1900;
          HEAP32[(((tmPtr)+(24))>>2)] = date.getDay();
      
          var yday = ydayFromDate(date)|0;
          HEAP32[(((tmPtr)+(28))>>2)] = yday;
          HEAP32[(((tmPtr)+(36))>>2)] = -(date.getTimezoneOffset() * 60);
      
          // Attention: DST is in December in South, and some regions don't have DST at all.
          var start = new Date(date.getFullYear(), 0, 1);
          var summerOffset = new Date(date.getFullYear(), 6, 1).getTimezoneOffset();
          var winterOffset = start.getTimezoneOffset();
          var dst = (summerOffset != winterOffset && date.getTimezoneOffset() == Math.min(winterOffset, summerOffset))|0;
          HEAP32[(((tmPtr)+(32))>>2)] = dst;
      }

      
      
      
      
      
      function __mmap_js(len, prot, flags, fd, offset, allocated, addr) {
        offset = bigintToI53Checked(offset);
      
        
      try {
      
          if (isNaN(offset)) return 61;
          var stream = SYSCALLS.getStreamFromFD(fd);
          var res = FS.mmap(stream, len, offset, prot, flags);
          var ptr = res.ptr;
          HEAP32[((allocated)>>2)] = res.allocated;
          HEAPU32[((addr)>>2)] = ptr;
          return 0;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      
      function __msync_js(addr, len, prot, flags, fd, offset) {
        offset = bigintToI53Checked(offset);
      
        
      try {
      
          if (isNaN(offset)) return 61;
          SYSCALLS.doMsync(addr, SYSCALLS.getStreamFromFD(fd), len, flags, offset);
          return 0;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      
      function __munmap_js(addr, len, prot, flags, fd, offset) {
        offset = bigintToI53Checked(offset);
      
        
      try {
      
          var stream = SYSCALLS.getStreamFromFD(fd);
          if (prot & 2) {
            SYSCALLS.doMsync(addr, stream, len, flags, offset);
          }
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return -e.errno;
      }
      }

      var __tzset_js = (timezone, daylight, std_name, dst_name) => {
          // TODO: Use (malleable) environment variables instead of system settings.
          var currentYear = new Date().getFullYear();
          var winter = new Date(currentYear, 0, 1);
          var summer = new Date(currentYear, 6, 1);
          var winterOffset = winter.getTimezoneOffset();
          var summerOffset = summer.getTimezoneOffset();
      
          // Local standard timezone offset. Local standard time is not adjusted for
          // daylight savings.  This code uses the fact that getTimezoneOffset returns
          // a greater value during Standard Time versus Daylight Saving Time (DST).
          // Thus it determines the expected output during Standard Time, and it
          // compares whether the output of the given date the same (Standard) or less
          // (DST).
          var stdTimezoneOffset = Math.max(winterOffset, summerOffset);
      
          // timezone is specified as seconds west of UTC ("The external variable
          // `timezone` shall be set to the difference, in seconds, between
          // Coordinated Universal Time (UTC) and local standard time."), the same
          // as returned by stdTimezoneOffset.
          // See http://pubs.opengroup.org/onlinepubs/009695399/functions/tzset.html
          HEAPU32[((timezone)>>2)] = stdTimezoneOffset * 60;
      
          HEAP32[((daylight)>>2)] = Number(winterOffset != summerOffset);
      
          function extractZone(date) {
            var match = date.toTimeString().match(/\(([A-Za-z ]+)\)$/);
            return match ? match[1] : "GMT";
          }      var winterName = extractZone(winter);
          var summerName = extractZone(summer);
          if (summerOffset < winterOffset) {
            // Northern hemisphere
            stringToUTF8(winterName, std_name, 7);
            stringToUTF8(summerName, dst_name, 7);
          } else {
            stringToUTF8(winterName, dst_name, 7);
            stringToUTF8(summerName, std_name, 7);
          }
        };

      var _abort = () => {
          abort('');
        };

      var _emscripten_date_now = () => Date.now();

      
      var runtimeKeepaliveCounter = 0;
      var keepRuntimeAlive = () => noExitRuntime || runtimeKeepaliveCounter > 0;
      var _proc_exit = (code) => {
          EXITSTATUS = code;
          if (!keepRuntimeAlive()) {
            Module['onExit']?.(code);
            ABORT = true;
          }
          quit_(code, new ExitStatus(code));
        };
      
      /** @suppress {duplicate } */
      /** @param {boolean|number=} implicit */
      var exitJS = (status, implicit) => {
          EXITSTATUS = status;
      
          if (!keepRuntimeAlive()) {
            exitRuntime();
          }
      
          _proc_exit(status);
        };
      var _exit = exitJS;
      
      var __emscripten_runtime_keepalive_clear = () => {
          noExitRuntime = false;
          runtimeKeepaliveCounter = 0;
        };
      
      var _emscripten_force_exit = (status) => {
          __emscripten_runtime_keepalive_clear();
          _exit(status);
        };
      Module['_emscripten_force_exit'] = _emscripten_force_exit;

      var getHeapMax = () =>
          // Stay one Wasm page short of 4GB: while e.g. Chrome is able to allocate
          // full 4GB Wasm memories, the size will wrap back to 0 bytes in Wasm side
          // for any code that deals with heap sizes, which would require special
          // casing all heap size related code to treat 0 specially.
          2147483648;
      var _emscripten_get_heap_max = () => getHeapMax();

      var _emscripten_get_now;
          // Modern environment where performance.now() is supported:
          // N.B. a shorter form "_emscripten_get_now = performance.now;" is
          // unfortunately not allowed even in current browsers (e.g. FF Nightly 75).
          _emscripten_get_now = () => performance.now();

      var _emscripten_get_now_res = () => { // return resolution of get_now, in nanoseconds
          if (ENVIRONMENT_IS_NODE) {
            return 1; // nanoseconds
          }
          // Modern environment where performance.now() is supported:
          return 1000; // microseconds (1/1000 of a millisecond)
        };

      
      var growMemory = (size) => {
          var b = wasmMemory.buffer;
          var pages = (size - b.byteLength + 65535) / 65536;
          try {
            // round size grow request up to wasm page size (fixed 64KB per spec)
            wasmMemory.grow(pages); // .grow() takes a delta compared to the previous size
            updateMemoryViews();
            return 1 /*success*/;
          } catch(e) {
          }
          // implicit 0 return to save code size (caller will cast "undefined" into 0
          // anyhow)
        };
      var _emscripten_resize_heap = (requestedSize) => {
          var oldSize = HEAPU8.length;
          // With CAN_ADDRESS_2GB or MEMORY64, pointers are already unsigned.
          requestedSize >>>= 0;
          // With multithreaded builds, races can happen (another thread might increase the size
          // in between), so return a failure, and let the caller retry.
      
          // Memory resize rules:
          // 1.  Always increase heap size to at least the requested size, rounded up
          //     to next page multiple.
          // 2a. If MEMORY_GROWTH_LINEAR_STEP == -1, excessively resize the heap
          //     geometrically: increase the heap size according to
          //     MEMORY_GROWTH_GEOMETRIC_STEP factor (default +20%), At most
          //     overreserve by MEMORY_GROWTH_GEOMETRIC_CAP bytes (default 96MB).
          // 2b. If MEMORY_GROWTH_LINEAR_STEP != -1, excessively resize the heap
          //     linearly: increase the heap size by at least
          //     MEMORY_GROWTH_LINEAR_STEP bytes.
          // 3.  Max size for the heap is capped at 2048MB-WASM_PAGE_SIZE, or by
          //     MAXIMUM_MEMORY, or by ASAN limit, depending on which is smallest
          // 4.  If we were unable to allocate as much memory, it may be due to
          //     over-eager decision to excessively reserve due to (3) above.
          //     Hence if an allocation fails, cut down on the amount of excess
          //     growth, in an attempt to succeed to perform a smaller allocation.
      
          // A limit is set for how much we can grow. We should not exceed that
          // (the wasm binary specifies it, so if we tried, we'd fail anyhow).
          var maxHeapSize = getHeapMax();
          if (requestedSize > maxHeapSize) {
            return false;
          }
      
          var alignUp = (x, multiple) => x + (multiple - x % multiple) % multiple;
      
          // Loop through potential heap size increases. If we attempt a too eager
          // reservation that fails, cut down on the attempted size and reserve a
          // smaller bump instead. (max 3 times, chosen somewhat arbitrarily)
          for (var cutDown = 1; cutDown <= 4; cutDown *= 2) {
            var overGrownHeapSize = oldSize * (1 + 0.2 / cutDown); // ensure geometric growth
            // but limit overreserving (default to capping at +96MB overgrowth at most)
            overGrownHeapSize = Math.min(overGrownHeapSize, requestedSize + 100663296 );
      
            var newSize = Math.min(maxHeapSize, alignUp(Math.max(requestedSize, overGrownHeapSize), 65536));
      
            var replacement = growMemory(newSize);
            if (replacement) {
      
              return true;
            }
          }
          return false;
        };

      var ENV = {
      };
      
      var getExecutableName = () => {
          return thisProgram || './this.program';
        };
      var getEnvStrings = () => {
          if (!getEnvStrings.strings) {
            // Default values.
            // Browser language detection #8751
            var lang = ((typeof navigator == 'object' && navigator.languages && navigator.languages[0]) || 'C').replace('-', '_') + '.UTF-8';
            var env = {
              'USER': 'web_user',
              'LOGNAME': 'web_user',
              'PATH': '/',
              'PWD': '/',
              'HOME': '/home/web_user',
              'LANG': lang,
              '_': getExecutableName()
            };
            // Apply the user-provided values, if any.
            for (var x in ENV) {
              // x is a key in ENV; if ENV[x] is undefined, that means it was
              // explicitly set to be so. We allow user code to do that to
              // force variables with default values to remain unset.
              if (ENV[x] === undefined) delete env[x];
              else env[x] = ENV[x];
            }
            var strings = [];
            for (var x in env) {
              strings.push(`${x}=${env[x]}`);
            }
            getEnvStrings.strings = strings;
          }
          return getEnvStrings.strings;
        };
      
      var stringToAscii = (str, buffer) => {
          for (var i = 0; i < str.length; ++i) {
            HEAP8[buffer++] = str.charCodeAt(i);
          }
          // Null-terminate the string
          HEAP8[buffer] = 0;
        };
      var _environ_get = (__environ, environ_buf) => {
          var bufSize = 0;
          getEnvStrings().forEach((string, i) => {
            var ptr = environ_buf + bufSize;
            HEAPU32[(((__environ)+(i*4))>>2)] = ptr;
            stringToAscii(string, ptr);
            bufSize += string.length + 1;
          });
          return 0;
        };

      var _environ_sizes_get = (penviron_count, penviron_buf_size) => {
          var strings = getEnvStrings();
          HEAPU32[((penviron_count)>>2)] = strings.length;
          var bufSize = 0;
          strings.forEach((string) => bufSize += string.length + 1);
          HEAPU32[((penviron_buf_size)>>2)] = bufSize;
          return 0;
        };


      function _fd_close(fd) {
      try {
      
          var stream = SYSCALLS.getStreamFromFD(fd);
          FS.close(stream);
          return 0;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return e.errno;
      }
      }

      function _fd_fdstat_get(fd, pbuf) {
      try {
      
          var rightsBase = 0;
          var rightsInheriting = 0;
          var flags = 0;
          {
            var stream = SYSCALLS.getStreamFromFD(fd);
            // All character devices are terminals (other things a Linux system would
            // assume is a character device, like the mouse, we have special APIs for).
            var type = stream.tty ? 2 :
                       FS.isDir(stream.mode) ? 3 :
                       FS.isLink(stream.mode) ? 7 :
                       4;
          }
          HEAP8[pbuf] = type;
          HEAP16[(((pbuf)+(2))>>1)] = flags;
          HEAP64[(((pbuf)+(8))>>3)] = BigInt(rightsBase);
          HEAP64[(((pbuf)+(16))>>3)] = BigInt(rightsInheriting);
          return 0;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return e.errno;
      }
      }

      /** @param {number=} offset */
      var doReadv = (stream, iov, iovcnt, offset) => {
          var ret = 0;
          for (var i = 0; i < iovcnt; i++) {
            var ptr = HEAPU32[((iov)>>2)];
            var len = HEAPU32[(((iov)+(4))>>2)];
            iov += 8;
            var curr = FS.read(stream, HEAP8, ptr, len, offset);
            if (curr < 0) return -1;
            ret += curr;
            if (curr < len) break; // nothing more to read
            if (typeof offset !== 'undefined') {
              offset += curr;
            }
          }
          return ret;
        };
      
      
      function _fd_pread(fd, iov, iovcnt, offset, pnum) {
        offset = bigintToI53Checked(offset);
      
        
      try {
      
          if (isNaN(offset)) return 61;
          var stream = SYSCALLS.getStreamFromFD(fd);
          var num = doReadv(stream, iov, iovcnt, offset);
          HEAPU32[((pnum)>>2)] = num;
          return 0;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return e.errno;
      }
      }

      /** @param {number=} offset */
      var doWritev = (stream, iov, iovcnt, offset) => {
          var ret = 0;
          for (var i = 0; i < iovcnt; i++) {
            var ptr = HEAPU32[((iov)>>2)];
            var len = HEAPU32[(((iov)+(4))>>2)];
            iov += 8;
            var curr = FS.write(stream, HEAP8, ptr, len, offset);
            if (curr < 0) return -1;
            ret += curr;
            if (typeof offset !== 'undefined') {
              offset += curr;
            }
          }
          return ret;
        };
      
      
      function _fd_pwrite(fd, iov, iovcnt, offset, pnum) {
        offset = bigintToI53Checked(offset);
      
        
      try {
      
          if (isNaN(offset)) return 61;
          var stream = SYSCALLS.getStreamFromFD(fd);
          var num = doWritev(stream, iov, iovcnt, offset);
          HEAPU32[((pnum)>>2)] = num;
          return 0;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return e.errno;
      }
      }

      
      function _fd_read(fd, iov, iovcnt, pnum) {
      try {
      
          var stream = SYSCALLS.getStreamFromFD(fd);
          var num = doReadv(stream, iov, iovcnt);
          HEAPU32[((pnum)>>2)] = num;
          return 0;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return e.errno;
      }
      }

      
      function _fd_seek(fd, offset, whence, newOffset) {
        offset = bigintToI53Checked(offset);
      
        
      try {
      
          if (isNaN(offset)) return 61;
          var stream = SYSCALLS.getStreamFromFD(fd);
          FS.llseek(stream, offset, whence);
          HEAP64[((newOffset)>>3)] = BigInt(stream.position);
          if (stream.getdents && offset === 0 && whence === 0) stream.getdents = null; // reset readdir state
          return 0;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return e.errno;
      }
      }

      function _fd_sync(fd) {
      try {
      
          var stream = SYSCALLS.getStreamFromFD(fd);
          if (stream.stream_ops?.fsync) {
            return stream.stream_ops.fsync(stream);
          }
          return 0; // we can't do anything synchronously; the in-memory FS is already synced to
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return e.errno;
      }
      }

      
      function _fd_write(fd, iov, iovcnt, pnum) {
      try {
      
          var stream = SYSCALLS.getStreamFromFD(fd);
          var num = doWritev(stream, iov, iovcnt);
          HEAPU32[((pnum)>>2)] = num;
          return 0;
        } catch (e) {
        if (typeof FS == 'undefined' || !(e.name === 'ErrnoError')) throw e;
        return e.errno;
      }
      }

      var DOTNET = {
      setup:function setup(emscriptenBuildOptions) {
          // USE_PTHREADS is emscripten's define symbol, which is passed to acorn optimizer, so we could use it here
          const modulePThread = {};
          const ENVIRONMENT_IS_PTHREAD = false;
          const dotnet_replacements = {
              fetch: globalThis.fetch,
              ENVIRONMENT_IS_WORKER,
              require,
              modulePThread,
              scriptDirectory,
          };
      
          ENVIRONMENT_IS_WORKER = dotnet_replacements.ENVIRONMENT_IS_WORKER;
          Module.__dotnet_runtime.initializeReplacements(dotnet_replacements);
          noExitRuntime = dotnet_replacements.noExitRuntime;
          fetch = dotnet_replacements.fetch;
          require = dotnet_replacements.require;
          _scriptDir = scriptDirectory = dotnet_replacements.scriptDirectory;
          Module.__dotnet_runtime.passEmscriptenInternals({
              isPThread: ENVIRONMENT_IS_PTHREAD,
              quit_, ExitStatus,
              updateMemoryViews,
              getMemory: () => { return wasmMemory; },
              getWasmIndirectFunctionTable: () => { return wasmTable; },
          }, emscriptenBuildOptions);
      
              Module.__dotnet_runtime.configureEmscriptenStartup(Module);
      },
      };
      function _mono_interp_flush_jitcall_queue(
      ) {
      return {runtime_idx:12};//mono_interp_flush_jitcall_queue
      }

      function _mono_interp_invoke_wasm_jit_call_trampoline(
      ) {
      return {runtime_idx:11};//mono_interp_invoke_wasm_jit_call_trampoline
      }

      function _mono_interp_jit_wasm_entry_trampoline(
      ) {
      return {runtime_idx:9};//mono_interp_jit_wasm_entry_trampoline
      }

      function _mono_interp_jit_wasm_jit_call_trampoline(
      ) {
      return {runtime_idx:10};//mono_interp_jit_wasm_jit_call_trampoline
      }

      function _mono_interp_record_interp_entry(
      ) {
      return {runtime_idx:8};//mono_interp_record_interp_entry
      }

      function _mono_interp_tier_prepare_jiterpreter(
      ) {
      return {runtime_idx:7};//mono_interp_tier_prepare_jiterpreter
      }

      function _mono_wasm_bind_js_import_ST(
      ) {
      return {runtime_idx:22};//mono_wasm_bind_js_import_ST
      }

      function _mono_wasm_browser_entropy(
      ) {
      return {runtime_idx:18};//mono_wasm_browser_entropy
      }

      function _mono_wasm_cancel_promise(
      ) {
      return {runtime_idx:26};//mono_wasm_cancel_promise
      }

      function _mono_wasm_console_clear(
      ) {
      return {runtime_idx:20};//mono_wasm_console_clear
      }

      function _mono_wasm_free_method_data(
      ) {
      return {runtime_idx:13};//mono_wasm_free_method_data
      }

      function _mono_wasm_get_locale_info(
      ) {
      return {runtime_idx:27};//mono_wasm_get_locale_info
      }

      function _mono_wasm_invoke_js_function(
      ) {
      return {runtime_idx:23};//mono_wasm_invoke_js_function
      }

      function _mono_wasm_invoke_jsimport_ST(
      ) {
      return {runtime_idx:24};//mono_wasm_invoke_jsimport_ST
      }

      function _mono_wasm_process_current_pid(
      ) {
      return {runtime_idx:19};//mono_wasm_process_current_pid
      }

      function _mono_wasm_release_cs_owned_object(
      ) {
      return {runtime_idx:21};//mono_wasm_release_cs_owned_object
      }

      function _mono_wasm_resolve_or_reject_promise(
      ) {
      return {runtime_idx:25};//mono_wasm_resolve_or_reject_promise
      }

      function _mono_wasm_schedule_timer(
      ) {
      return {runtime_idx:0};//mono_wasm_schedule_timer
      }

      function _mono_wasm_set_entrypoint_breakpoint(
      ) {
      return {runtime_idx:17};//mono_wasm_set_entrypoint_breakpoint
      }

      function _mono_wasm_trace_logger(
      ) {
      return {runtime_idx:16};//mono_wasm_trace_logger
      }

      function _schedule_background_exec(
      ) {
      return {runtime_idx:6};//schedule_background_exec
      }

      
      var arraySum = (array, index) => {
          var sum = 0;
          for (var i = 0; i <= index; sum += array[i++]) {
            // no-op
          }
          return sum;
        };
      
      
      var MONTH_DAYS_LEAP = [31,29,31,30,31,30,31,31,30,31,30,31];
      
      var MONTH_DAYS_REGULAR = [31,28,31,30,31,30,31,31,30,31,30,31];
      var addDays = (date, days) => {
          var newDate = new Date(date.getTime());
          while (days > 0) {
            var leap = isLeapYear(newDate.getFullYear());
            var currentMonth = newDate.getMonth();
            var daysInCurrentMonth = (leap ? MONTH_DAYS_LEAP : MONTH_DAYS_REGULAR)[currentMonth];
      
            if (days > daysInCurrentMonth-newDate.getDate()) {
              // we spill over to next month
              days -= (daysInCurrentMonth-newDate.getDate()+1);
              newDate.setDate(1);
              if (currentMonth < 11) {
                newDate.setMonth(currentMonth+1);
              } else {
                newDate.setMonth(0);
                newDate.setFullYear(newDate.getFullYear()+1);
              }
            } else {
              // we stay in current month
              newDate.setDate(newDate.getDate()+days);
              return newDate;
            }
          }
      
          return newDate;
        };
      
      
      
      
      var writeArrayToMemory = (array, buffer) => {
          HEAP8.set(array, buffer);
        };
      
      var _strftime = (s, maxsize, format, tm) => {
          // size_t strftime(char *restrict s, size_t maxsize, const char *restrict format, const struct tm *restrict timeptr);
          // http://pubs.opengroup.org/onlinepubs/009695399/functions/strftime.html
      
          var tm_zone = HEAPU32[(((tm)+(40))>>2)];
      
          var date = {
            tm_sec: HEAP32[((tm)>>2)],
            tm_min: HEAP32[(((tm)+(4))>>2)],
            tm_hour: HEAP32[(((tm)+(8))>>2)],
            tm_mday: HEAP32[(((tm)+(12))>>2)],
            tm_mon: HEAP32[(((tm)+(16))>>2)],
            tm_year: HEAP32[(((tm)+(20))>>2)],
            tm_wday: HEAP32[(((tm)+(24))>>2)],
            tm_yday: HEAP32[(((tm)+(28))>>2)],
            tm_isdst: HEAP32[(((tm)+(32))>>2)],
            tm_gmtoff: HEAP32[(((tm)+(36))>>2)],
            tm_zone: tm_zone ? UTF8ToString(tm_zone) : ''
          };
          
      
          var pattern = UTF8ToString(format);
      
          // expand format
          var EXPANSION_RULES_1 = {
            '%c': '%a %b %d %H:%M:%S %Y',     // Replaced by the locale's appropriate date and time representation - e.g., Mon Aug  3 14:02:01 2013
            '%D': '%m/%d/%y',                 // Equivalent to %m / %d / %y
            '%F': '%Y-%m-%d',                 // Equivalent to %Y - %m - %d
            '%h': '%b',                       // Equivalent to %b
            '%r': '%I:%M:%S %p',              // Replaced by the time in a.m. and p.m. notation
            '%R': '%H:%M',                    // Replaced by the time in 24-hour notation
            '%T': '%H:%M:%S',                 // Replaced by the time
            '%x': '%m/%d/%y',                 // Replaced by the locale's appropriate date representation
            '%X': '%H:%M:%S',                 // Replaced by the locale's appropriate time representation
            // Modified Conversion Specifiers
            '%Ec': '%c',                      // Replaced by the locale's alternative appropriate date and time representation.
            '%EC': '%C',                      // Replaced by the name of the base year (period) in the locale's alternative representation.
            '%Ex': '%m/%d/%y',                // Replaced by the locale's alternative date representation.
            '%EX': '%H:%M:%S',                // Replaced by the locale's alternative time representation.
            '%Ey': '%y',                      // Replaced by the offset from %EC (year only) in the locale's alternative representation.
            '%EY': '%Y',                      // Replaced by the full alternative year representation.
            '%Od': '%d',                      // Replaced by the day of the month, using the locale's alternative numeric symbols, filled as needed with leading zeros if there is any alternative symbol for zero; otherwise, with leading <space> characters.
            '%Oe': '%e',                      // Replaced by the day of the month, using the locale's alternative numeric symbols, filled as needed with leading <space> characters.
            '%OH': '%H',                      // Replaced by the hour (24-hour clock) using the locale's alternative numeric symbols.
            '%OI': '%I',                      // Replaced by the hour (12-hour clock) using the locale's alternative numeric symbols.
            '%Om': '%m',                      // Replaced by the month using the locale's alternative numeric symbols.
            '%OM': '%M',                      // Replaced by the minutes using the locale's alternative numeric symbols.
            '%OS': '%S',                      // Replaced by the seconds using the locale's alternative numeric symbols.
            '%Ou': '%u',                      // Replaced by the weekday as a number in the locale's alternative representation (Monday=1).
            '%OU': '%U',                      // Replaced by the week number of the year (Sunday as the first day of the week, rules corresponding to %U ) using the locale's alternative numeric symbols.
            '%OV': '%V',                      // Replaced by the week number of the year (Monday as the first day of the week, rules corresponding to %V ) using the locale's alternative numeric symbols.
            '%Ow': '%w',                      // Replaced by the number of the weekday (Sunday=0) using the locale's alternative numeric symbols.
            '%OW': '%W',                      // Replaced by the week number of the year (Monday as the first day of the week) using the locale's alternative numeric symbols.
            '%Oy': '%y',                      // Replaced by the year (offset from %C ) using the locale's alternative numeric symbols.
          };
          for (var rule in EXPANSION_RULES_1) {
            pattern = pattern.replace(new RegExp(rule, 'g'), EXPANSION_RULES_1[rule]);
          }
      
          var WEEKDAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
          var MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
      
          function leadingSomething(value, digits, character) {
            var str = typeof value == 'number' ? value.toString() : (value || '');
            while (str.length < digits) {
              str = character[0]+str;
            }
            return str;
          }
      
          function leadingNulls(value, digits) {
            return leadingSomething(value, digits, '0');
          }
      
          function compareByDay(date1, date2) {
            function sgn(value) {
              return value < 0 ? -1 : (value > 0 ? 1 : 0);
            }
      
            var compare;
            if ((compare = sgn(date1.getFullYear()-date2.getFullYear())) === 0) {
              if ((compare = sgn(date1.getMonth()-date2.getMonth())) === 0) {
                compare = sgn(date1.getDate()-date2.getDate());
              }
            }
            return compare;
          }
      
          function getFirstWeekStartDate(janFourth) {
              switch (janFourth.getDay()) {
                case 0: // Sunday
                  return new Date(janFourth.getFullYear()-1, 11, 29);
                case 1: // Monday
                  return janFourth;
                case 2: // Tuesday
                  return new Date(janFourth.getFullYear(), 0, 3);
                case 3: // Wednesday
                  return new Date(janFourth.getFullYear(), 0, 2);
                case 4: // Thursday
                  return new Date(janFourth.getFullYear(), 0, 1);
                case 5: // Friday
                  return new Date(janFourth.getFullYear()-1, 11, 31);
                case 6: // Saturday
                  return new Date(janFourth.getFullYear()-1, 11, 30);
              }
          }
      
          function getWeekBasedYear(date) {
              var thisDate = addDays(new Date(date.tm_year+1900, 0, 1), date.tm_yday);
      
              var janFourthThisYear = new Date(thisDate.getFullYear(), 0, 4);
              var janFourthNextYear = new Date(thisDate.getFullYear()+1, 0, 4);
      
              var firstWeekStartThisYear = getFirstWeekStartDate(janFourthThisYear);
              var firstWeekStartNextYear = getFirstWeekStartDate(janFourthNextYear);
      
              if (compareByDay(firstWeekStartThisYear, thisDate) <= 0) {
                // this date is after the start of the first week of this year
                if (compareByDay(firstWeekStartNextYear, thisDate) <= 0) {
                  return thisDate.getFullYear()+1;
                }
                return thisDate.getFullYear();
              }
              return thisDate.getFullYear()-1;
          }
      
          var EXPANSION_RULES_2 = {
            '%a': (date) => WEEKDAYS[date.tm_wday].substring(0,3) ,
            '%A': (date) => WEEKDAYS[date.tm_wday],
            '%b': (date) => MONTHS[date.tm_mon].substring(0,3),
            '%B': (date) => MONTHS[date.tm_mon],
            '%C': (date) => {
              var year = date.tm_year+1900;
              return leadingNulls((year/100)|0,2);
            },
            '%d': (date) => leadingNulls(date.tm_mday, 2),
            '%e': (date) => leadingSomething(date.tm_mday, 2, ' '),
            '%g': (date) => {
              // %g, %G, and %V give values according to the ISO 8601:2000 standard week-based year.
              // In this system, weeks begin on a Monday and week 1 of the year is the week that includes
              // January 4th, which is also the week that includes the first Thursday of the year, and
              // is also the first week that contains at least four days in the year.
              // If the first Monday of January is the 2nd, 3rd, or 4th, the preceding days are part of
              // the last week of the preceding year; thus, for Saturday 2nd January 1999,
              // %G is replaced by 1998 and %V is replaced by 53. If December 29th, 30th,
              // or 31st is a Monday, it and any following days are part of week 1 of the following year.
              // Thus, for Tuesday 30th December 1997, %G is replaced by 1998 and %V is replaced by 01.
      
              return getWeekBasedYear(date).toString().substring(2);
            },
            '%G': getWeekBasedYear,
            '%H': (date) => leadingNulls(date.tm_hour, 2),
            '%I': (date) => {
              var twelveHour = date.tm_hour;
              if (twelveHour == 0) twelveHour = 12;
              else if (twelveHour > 12) twelveHour -= 12;
              return leadingNulls(twelveHour, 2);
            },
            '%j': (date) => {
              // Day of the year (001-366)
              return leadingNulls(date.tm_mday + arraySum(isLeapYear(date.tm_year+1900) ? MONTH_DAYS_LEAP : MONTH_DAYS_REGULAR, date.tm_mon-1), 3);
            },
            '%m': (date) => leadingNulls(date.tm_mon+1, 2),
            '%M': (date) => leadingNulls(date.tm_min, 2),
            '%n': () => '\n',
            '%p': (date) => {
              if (date.tm_hour >= 0 && date.tm_hour < 12) {
                return 'AM';
              }
              return 'PM';
            },
            '%S': (date) => leadingNulls(date.tm_sec, 2),
            '%t': () => '\t',
            '%u': (date) => date.tm_wday || 7,
            '%U': (date) => {
              var days = date.tm_yday + 7 - date.tm_wday;
              return leadingNulls(Math.floor(days / 7), 2);
            },
            '%V': (date) => {
              // Replaced by the week number of the year (Monday as the first day of the week)
              // as a decimal number [01,53]. If the week containing 1 January has four
              // or more days in the new year, then it is considered week 1.
              // Otherwise, it is the last week of the previous year, and the next week is week 1.
              // Both January 4th and the first Thursday of January are always in week 1. [ tm_year, tm_wday, tm_yday]
              var val = Math.floor((date.tm_yday + 7 - (date.tm_wday + 6) % 7 ) / 7);
              // If 1 Jan is just 1-3 days past Monday, the previous week
              // is also in this year.
              if ((date.tm_wday + 371 - date.tm_yday - 2) % 7 <= 2) {
                val++;
              }
              if (!val) {
                val = 52;
                // If 31 December of prev year a Thursday, or Friday of a
                // leap year, then the prev year has 53 weeks.
                var dec31 = (date.tm_wday + 7 - date.tm_yday - 1) % 7;
                if (dec31 == 4 || (dec31 == 5 && isLeapYear(date.tm_year%400-1))) {
                  val++;
                }
              } else if (val == 53) {
                // If 1 January is not a Thursday, and not a Wednesday of a
                // leap year, then this year has only 52 weeks.
                var jan1 = (date.tm_wday + 371 - date.tm_yday) % 7;
                if (jan1 != 4 && (jan1 != 3 || !isLeapYear(date.tm_year)))
                  val = 1;
              }
              return leadingNulls(val, 2);
            },
            '%w': (date) => date.tm_wday,
            '%W': (date) => {
              var days = date.tm_yday + 7 - ((date.tm_wday + 6) % 7);
              return leadingNulls(Math.floor(days / 7), 2);
            },
            '%y': (date) => {
              // Replaced by the last two digits of the year as a decimal number [00,99]. [ tm_year]
              return (date.tm_year+1900).toString().substring(2);
            },
            // Replaced by the year as a decimal number (for example, 1997). [ tm_year]
            '%Y': (date) => date.tm_year+1900,
            '%z': (date) => {
              // Replaced by the offset from UTC in the ISO 8601:2000 standard format ( +hhmm or -hhmm ).
              // For example, "-0430" means 4 hours 30 minutes behind UTC (west of Greenwich).
              var off = date.tm_gmtoff;
              var ahead = off >= 0;
              off = Math.abs(off) / 60;
              // convert from minutes into hhmm format (which means 60 minutes = 100 units)
              off = (off / 60)*100 + (off % 60);
              return (ahead ? '+' : '-') + String("0000" + off).slice(-4);
            },
            '%Z': (date) => date.tm_zone,
            '%%': () => '%'
          };
      
          // Replace %% with a pair of NULLs (which cannot occur in a C string), then
          // re-inject them after processing.
          pattern = pattern.replace(/%%/g, '\0\0');
          for (var rule in EXPANSION_RULES_2) {
            if (pattern.includes(rule)) {
              pattern = pattern.replace(new RegExp(rule, 'g'), EXPANSION_RULES_2[rule](date));
            }
          }
          pattern = pattern.replace(/\0\0/g, '%');
      
          var bytes = intArrayFromString(pattern, false);
          if (bytes.length > maxsize) {
            return 0;
          }
      
          writeArrayToMemory(bytes, s);
          return bytes.length-1;
        };



      var getCFunc = (ident) => {
          var func = Module['_' + ident]; // closure exported function
          return func;
        };
      
      
      
      var stringToUTF8OnStack = (str) => {
          var size = lengthBytesUTF8(str) + 1;
          var ret = stackAlloc(size);
          stringToUTF8(str, ret, size);
          return ret;
        };
      
      
      
      
      
        /**
         * @param {string|null=} returnType
         * @param {Array=} argTypes
         * @param {Arguments|Array=} args
         * @param {Object=} opts
         */
      var ccall = (ident, returnType, argTypes, args, opts) => {
          // For fast lookup of conversion functions
          var toC = {
            'string': (str) => {
              var ret = 0;
              if (str !== null && str !== undefined && str !== 0) { // null string
                // at most 4 bytes per UTF-8 code point, +1 for the trailing '\0'
                ret = stringToUTF8OnStack(str);
              }
              return ret;
            },
            'array': (arr) => {
              var ret = stackAlloc(arr.length);
              writeArrayToMemory(arr, ret);
              return ret;
            }
          };
      
          function convertReturnValue(ret) {
            if (returnType === 'string') {
              
              return UTF8ToString(ret);
            }
            if (returnType === 'boolean') return Boolean(ret);
            return ret;
          }
      
          var func = getCFunc(ident);
          var cArgs = [];
          var stack = 0;
          if (args) {
            for (var i = 0; i < args.length; i++) {
              var converter = toC[argTypes[i]];
              if (converter) {
                if (stack === 0) stack = stackSave();
                cArgs[i] = converter(args[i]);
              } else {
                cArgs[i] = args[i];
              }
            }
          }
          var ret = func(...cArgs);
          function onDone(ret) {
            if (stack !== 0) stackRestore(stack);
            return convertReturnValue(ret);
          }
      
          ret = onDone(ret);
          return ret;
        };

      
      
        /**
         * @param {string=} returnType
         * @param {Array=} argTypes
         * @param {Object=} opts
         */
      var cwrap = (ident, returnType, argTypes, opts) => {
          // When the function takes numbers and returns a number, we can just return
          // the original function
          var numericArgs = !argTypes || argTypes.every((type) => type === 'number' || type === 'boolean');
          var numericRet = returnType !== 'string';
          if (numericRet && numericArgs && !opts) {
            return getCFunc(ident);
          }
          return (...args) => ccall(ident, returnType, argTypes, args);
        };








      var uleb128Encode = (n, target) => {
          if (n < 128) {
            target.push(n);
          } else {
            target.push((n % 128) | 128, n >> 7);
          }
        };
      
      var sigToWasmTypes = (sig) => {
          var typeNames = {
            'i': 'i32',
            'j': 'i64',
            'f': 'f32',
            'd': 'f64',
            'e': 'externref',
            'p': 'i32',
          };
          var type = {
            parameters: [],
            results: sig[0] == 'v' ? [] : [typeNames[sig[0]]]
          };
          for (var i = 1; i < sig.length; ++i) {
            type.parameters.push(typeNames[sig[i]]);
          }
          return type;
        };
      
      var generateFuncType = (sig, target) => {
          var sigRet = sig.slice(0, 1);
          var sigParam = sig.slice(1);
          var typeCodes = {
            'i': 0x7f, // i32
            'p': 0x7f, // i32
            'j': 0x7e, // i64
            'f': 0x7d, // f32
            'd': 0x7c, // f64
            'e': 0x6f, // externref
          };
      
          // Parameters, length + signatures
          target.push(0x60 /* form: func */);
          uleb128Encode(sigParam.length, target);
          for (var i = 0; i < sigParam.length; ++i) {
            target.push(typeCodes[sigParam[i]]);
          }
      
          // Return values, length + signatures
          // With no multi-return in MVP, either 0 (void) or 1 (anything else)
          if (sigRet == 'v') {
            target.push(0x00);
          } else {
            target.push(0x01, typeCodes[sigRet]);
          }
        };
      var convertJsFunctionToWasm = (func, sig) => {
      
          // If the type reflection proposal is available, use the new
          // "WebAssembly.Function" constructor.
          // Otherwise, construct a minimal wasm module importing the JS function and
          // re-exporting it.
          if (typeof WebAssembly.Function == "function") {
            return new WebAssembly.Function(sigToWasmTypes(sig), func);
          }
      
          // The module is static, with the exception of the type section, which is
          // generated based on the signature passed in.
          var typeSectionBody = [
            0x01, // count: 1
          ];
          generateFuncType(sig, typeSectionBody);
      
          // Rest of the module is static
          var bytes = [
            0x00, 0x61, 0x73, 0x6d, // magic ("\0asm")
            0x01, 0x00, 0x00, 0x00, // version: 1
            0x01, // Type section code
          ];
          // Write the overall length of the type section followed by the body
          uleb128Encode(typeSectionBody.length, bytes);
          bytes.push(...typeSectionBody);
      
          // The rest of the module is static
          bytes.push(
            0x02, 0x07, // import section
              // (import "e" "f" (func 0 (type 0)))
              0x01, 0x01, 0x65, 0x01, 0x66, 0x00, 0x00,
            0x07, 0x05, // export section
              // (export "f" (func 0 (type 0)))
              0x01, 0x01, 0x66, 0x00, 0x00,
          );
      
          // We can compile this wasm module synchronously because it is very small.
          // This accepts an import (at "e.f"), that it reroutes to an export (at "f")
          var module = new WebAssembly.Module(new Uint8Array(bytes));
          var instance = new WebAssembly.Instance(module, { 'e': { 'f': func } });
          var wrappedFunc = instance.exports['f'];
          return wrappedFunc;
        };
      
      var wasmTableMirror = [];
      
      var wasmTable;
      var getWasmTableEntry = (funcPtr) => {
          var func = wasmTableMirror[funcPtr];
          if (!func) {
            if (funcPtr >= wasmTableMirror.length) wasmTableMirror.length = funcPtr + 1;
            wasmTableMirror[funcPtr] = func = wasmTable.get(funcPtr);
          }
          return func;
        };
      
      var updateTableMap = (offset, count) => {
          if (functionsInTableMap) {
            for (var i = offset; i < offset + count; i++) {
              var item = getWasmTableEntry(i);
              // Ignore null values.
              if (item) {
                functionsInTableMap.set(item, i);
              }
            }
          }
        };
      
      var functionsInTableMap;
      
      var getFunctionAddress = (func) => {
          // First, create the map if this is the first use.
          if (!functionsInTableMap) {
            functionsInTableMap = new WeakMap();
            updateTableMap(0, wasmTable.length);
          }
          return functionsInTableMap.get(func) || 0;
        };
      
      
      var freeTableIndexes = [];
      
      var getEmptyTableSlot = () => {
          // Reuse a free index if there is one, otherwise grow.
          if (freeTableIndexes.length) {
            return freeTableIndexes.pop();
          }
          // Grow the table
          try {
            wasmTable.grow(1);
          } catch (err) {
            if (!(err instanceof RangeError)) {
              throw err;
            }
            throw 'Unable to grow wasm table. Set ALLOW_TABLE_GROWTH.';
          }
          return wasmTable.length - 1;
        };
      
      
      
      var setWasmTableEntry = (idx, func) => {
          wasmTable.set(idx, func);
          // With ABORT_ON_WASM_EXCEPTIONS wasmTable.get is overridden to return wrapped
          // functions so we need to call it here to retrieve the potential wrapper correctly
          // instead of just storing 'func' directly into wasmTableMirror
          wasmTableMirror[idx] = wasmTable.get(idx);
        };
      
      /** @param {string=} sig */
      var addFunction = (func, sig) => {
          // Check if the function is already in the table, to ensure each function
          // gets a unique index.
          var rtn = getFunctionAddress(func);
          if (rtn) {
            return rtn;
          }
      
          // It's not in the table, add it now.
      
          var ret = getEmptyTableSlot();
      
          // Set the new value.
          try {
            // Attempting to call this with JS function will cause of table.set() to fail
            setWasmTableEntry(ret, func);
          } catch (err) {
            if (!(err instanceof TypeError)) {
              throw err;
            }
            var wrapped = convertJsFunctionToWasm(func, sig);
            setWasmTableEntry(ret, wrapped);
          }
      
          functionsInTableMap.set(func, ret);
      
          return ret;
        };

      var handleException = (e) => {
          // Certain exception types we do not treat as errors since they are used for
          // internal control flow.
          // 1. ExitStatus, which is thrown by exit()
          // 2. "unwind", which is thrown by emscripten_unwind_to_js_event_loop() and others
          //    that wish to return to JS event loop.
          if (e instanceof ExitStatus || e == 'unwind') {
            return EXITSTATUS;
          }
          quit_(1, e);
        };
      
      
      
      var maybeExit = () => {
          if (runtimeExited) {
            return;
          }
          if (!keepRuntimeAlive()) {
            try {
              _exit(EXITSTATUS);
            } catch (e) {
              handleException(e);
            }
          }
        };
      var callUserCallback = (func) => {
          if (runtimeExited || ABORT) {
            return;
          }
          try {
            func();
            maybeExit();
          } catch (e) {
            handleException(e);
          }
        };
      
      var runtimeKeepalivePush = () => {
          runtimeKeepaliveCounter += 1;
        };
      
      var runtimeKeepalivePop = () => {
          runtimeKeepaliveCounter -= 1;
        };
      /** @param {number=} timeout */
      var safeSetTimeout = (func, timeout) => {
          runtimeKeepalivePush();
          return setTimeout(() => {
            runtimeKeepalivePop();
            callUserCallback(func);
          }, timeout);
        };

      FS.createPreloadedFile = FS_createPreloadedFile;
      FS.staticInit();Module["FS_createPath"] = FS.createPath;Module["FS_createDataFile"] = FS.createDataFile;Module["FS_createPath"] = FS.createPath;Module["FS_createDataFile"] = FS.createDataFile;Module["FS_createPreloadedFile"] = FS.createPreloadedFile;Module["FS_unlink"] = FS.unlink;Module["FS_createLazyFile"] = FS.createLazyFile;Module["FS_createDevice"] = FS.createDevice;DOTNET.setup({ wasmEnableSIMD: true,wasmEnableEH: true,enableAotProfiler: false, enableDevToolsProfiler: false, enableLogProfiler: false, enableEventPipe: false, runAOTCompilation: false, wasmEnableThreads: false, gitHash: "e2f47b0110ed922f21a1522da67279133ce28f32", });var wasmImports = {
      /** @export */
      __assert_fail: ___assert_fail,
      /** @export */
      __syscall_chdir: ___syscall_chdir,
      /** @export */
      __syscall_chmod: ___syscall_chmod,
      /** @export */
      __syscall_connect: ___syscall_connect,
      /** @export */
      __syscall_faccessat: ___syscall_faccessat,
      /** @export */
      __syscall_fadvise64: ___syscall_fadvise64,
      /** @export */
      __syscall_fchmod: ___syscall_fchmod,
      /** @export */
      __syscall_fcntl64: ___syscall_fcntl64,
      /** @export */
      __syscall_fstat64: ___syscall_fstat64,
      /** @export */
      __syscall_fstatfs64: ___syscall_fstatfs64,
      /** @export */
      __syscall_ftruncate64: ___syscall_ftruncate64,
      /** @export */
      __syscall_getcwd: ___syscall_getcwd,
      /** @export */
      __syscall_getdents64: ___syscall_getdents64,
      /** @export */
      __syscall_ioctl: ___syscall_ioctl,
      /** @export */
      __syscall_lstat64: ___syscall_lstat64,
      /** @export */
      __syscall_mkdirat: ___syscall_mkdirat,
      /** @export */
      __syscall_newfstatat: ___syscall_newfstatat,
      /** @export */
      __syscall_openat: ___syscall_openat,
      /** @export */
      __syscall_readlinkat: ___syscall_readlinkat,
      /** @export */
      __syscall_renameat: ___syscall_renameat,
      /** @export */
      __syscall_rmdir: ___syscall_rmdir,
      /** @export */
      __syscall_sendto: ___syscall_sendto,
      /** @export */
      __syscall_socket: ___syscall_socket,
      /** @export */
      __syscall_stat64: ___syscall_stat64,
      /** @export */
      __syscall_symlink: ___syscall_symlink,
      /** @export */
      __syscall_unlinkat: ___syscall_unlinkat,
      /** @export */
      __syscall_utimensat: ___syscall_utimensat,
      /** @export */
      _emscripten_get_now_is_monotonic: __emscripten_get_now_is_monotonic,
      /** @export */
      _gmtime_js: __gmtime_js,
      /** @export */
      _localtime_js: __localtime_js,
      /** @export */
      _mmap_js: __mmap_js,
      /** @export */
      _msync_js: __msync_js,
      /** @export */
      _munmap_js: __munmap_js,
      /** @export */
      _tzset_js: __tzset_js,
      /** @export */
      abort: _abort,
      /** @export */
      emscripten_date_now: _emscripten_date_now,
      /** @export */
      emscripten_force_exit: _emscripten_force_exit,
      /** @export */
      emscripten_get_heap_max: _emscripten_get_heap_max,
      /** @export */
      emscripten_get_now: _emscripten_get_now,
      /** @export */
      emscripten_get_now_res: _emscripten_get_now_res,
      /** @export */
      emscripten_resize_heap: _emscripten_resize_heap,
      /** @export */
      environ_get: _environ_get,
      /** @export */
      environ_sizes_get: _environ_sizes_get,
      /** @export */
      exit: _exit,
      /** @export */
      fd_close: _fd_close,
      /** @export */
      fd_fdstat_get: _fd_fdstat_get,
      /** @export */
      fd_pread: _fd_pread,
      /** @export */
      fd_pwrite: _fd_pwrite,
      /** @export */
      fd_read: _fd_read,
      /** @export */
      fd_seek: _fd_seek,
      /** @export */
      fd_sync: _fd_sync,
      /** @export */
      fd_write: _fd_write,
      /** @export */
      mono_interp_flush_jitcall_queue: _mono_interp_flush_jitcall_queue,
      /** @export */
      mono_interp_invoke_wasm_jit_call_trampoline: _mono_interp_invoke_wasm_jit_call_trampoline,
      /** @export */
      mono_interp_jit_wasm_entry_trampoline: _mono_interp_jit_wasm_entry_trampoline,
      /** @export */
      mono_interp_jit_wasm_jit_call_trampoline: _mono_interp_jit_wasm_jit_call_trampoline,
      /** @export */
      mono_interp_record_interp_entry: _mono_interp_record_interp_entry,
      /** @export */
      mono_interp_tier_prepare_jiterpreter: _mono_interp_tier_prepare_jiterpreter,
      /** @export */
      mono_wasm_bind_js_import_ST: _mono_wasm_bind_js_import_ST,
      /** @export */
      mono_wasm_browser_entropy: _mono_wasm_browser_entropy,
      /** @export */
      mono_wasm_cancel_promise: _mono_wasm_cancel_promise,
      /** @export */
      mono_wasm_console_clear: _mono_wasm_console_clear,
      /** @export */
      mono_wasm_free_method_data: _mono_wasm_free_method_data,
      /** @export */
      mono_wasm_get_locale_info: _mono_wasm_get_locale_info,
      /** @export */
      mono_wasm_invoke_js_function: _mono_wasm_invoke_js_function,
      /** @export */
      mono_wasm_invoke_jsimport_ST: _mono_wasm_invoke_jsimport_ST,
      /** @export */
      mono_wasm_process_current_pid: _mono_wasm_process_current_pid,
      /** @export */
      mono_wasm_release_cs_owned_object: _mono_wasm_release_cs_owned_object,
      /** @export */
      mono_wasm_resolve_or_reject_promise: _mono_wasm_resolve_or_reject_promise,
      /** @export */
      mono_wasm_schedule_timer: _mono_wasm_schedule_timer,
      /** @export */
      mono_wasm_set_entrypoint_breakpoint: _mono_wasm_set_entrypoint_breakpoint,
      /** @export */
      mono_wasm_trace_logger: _mono_wasm_trace_logger,
      /** @export */
      schedule_background_exec: _schedule_background_exec,
      /** @export */
      strftime: _strftime
    };
    var wasmExports = createWasm();
    Module['_mono_wasm_register_root'] = (a0, a1, a2) => (Module['_mono_wasm_register_root'] = wasmExports['mono_wasm_register_root'])(a0, a1, a2);
    Module['_mono_wasm_deregister_root'] = (a0) => (Module['_mono_wasm_deregister_root'] = wasmExports['mono_wasm_deregister_root'])(a0);
    Module['_mono_wasm_add_assembly'] = (a0, a1, a2) => (Module['_mono_wasm_add_assembly'] = wasmExports['mono_wasm_add_assembly'])(a0, a1, a2);
    Module['_mono_wasm_add_satellite_assembly'] = (a0, a1, a2, a3) => (Module['_mono_wasm_add_satellite_assembly'] = wasmExports['mono_wasm_add_satellite_assembly'])(a0, a1, a2, a3);
    Module['_malloc'] = (a0) => (Module['_malloc'] = wasmExports['malloc'])(a0);
    Module['_mono_wasm_setenv'] = (a0, a1) => (Module['_mono_wasm_setenv'] = wasmExports['mono_wasm_setenv'])(a0, a1);
    Module['_mono_wasm_getenv'] = (a0) => (Module['_mono_wasm_getenv'] = wasmExports['mono_wasm_getenv'])(a0);
    Module['_free'] = (a0) => (Module['_free'] = wasmExports['free'])(a0);
    Module['_mono_wasm_load_runtime'] = (a0, a1, a2, a3) => (Module['_mono_wasm_load_runtime'] = wasmExports['mono_wasm_load_runtime'])(a0, a1, a2, a3);
    Module['_mono_wasm_invoke_jsexport'] = (a0, a1) => (Module['_mono_wasm_invoke_jsexport'] = wasmExports['mono_wasm_invoke_jsexport'])(a0, a1);
    Module['_mono_wasm_string_from_utf16_ref'] = (a0, a1, a2) => (Module['_mono_wasm_string_from_utf16_ref'] = wasmExports['mono_wasm_string_from_utf16_ref'])(a0, a1, a2);
    Module['_mono_wasm_exec_regression'] = (a0, a1) => (Module['_mono_wasm_exec_regression'] = wasmExports['mono_wasm_exec_regression'])(a0, a1);
    Module['_mono_wasm_exit'] = (a0) => (Module['_mono_wasm_exit'] = wasmExports['mono_wasm_exit'])(a0);
    var _fflush = (a0) => (_fflush = wasmExports['fflush'])(a0);
    Module['_mono_wasm_set_main_args'] = (a0, a1) => (Module['_mono_wasm_set_main_args'] = wasmExports['mono_wasm_set_main_args'])(a0, a1);
    Module['_mono_wasm_strdup'] = (a0) => (Module['_mono_wasm_strdup'] = wasmExports['mono_wasm_strdup'])(a0);
    Module['_mono_wasm_parse_runtime_options'] = (a0, a1) => (Module['_mono_wasm_parse_runtime_options'] = wasmExports['mono_wasm_parse_runtime_options'])(a0, a1);
    Module['_mono_wasm_intern_string_ref'] = (a0) => (Module['_mono_wasm_intern_string_ref'] = wasmExports['mono_wasm_intern_string_ref'])(a0);
    Module['_mono_wasm_string_get_data_ref'] = (a0, a1, a2, a3) => (Module['_mono_wasm_string_get_data_ref'] = wasmExports['mono_wasm_string_get_data_ref'])(a0, a1, a2, a3);
    Module['_mono_wasm_write_managed_pointer_unsafe'] = (a0, a1) => (Module['_mono_wasm_write_managed_pointer_unsafe'] = wasmExports['mono_wasm_write_managed_pointer_unsafe'])(a0, a1);
    Module['_mono_wasm_copy_managed_pointer'] = (a0, a1) => (Module['_mono_wasm_copy_managed_pointer'] = wasmExports['mono_wasm_copy_managed_pointer'])(a0, a1);
    Module['_mono_wasm_init_finalizer_thread'] = () => (Module['_mono_wasm_init_finalizer_thread'] = wasmExports['mono_wasm_init_finalizer_thread'])();
    Module['_mono_wasm_i52_to_f64'] = (a0, a1) => (Module['_mono_wasm_i52_to_f64'] = wasmExports['mono_wasm_i52_to_f64'])(a0, a1);
    Module['_mono_wasm_u52_to_f64'] = (a0, a1) => (Module['_mono_wasm_u52_to_f64'] = wasmExports['mono_wasm_u52_to_f64'])(a0, a1);
    Module['_mono_wasm_f64_to_u52'] = (a0, a1) => (Module['_mono_wasm_f64_to_u52'] = wasmExports['mono_wasm_f64_to_u52'])(a0, a1);
    Module['_mono_wasm_f64_to_i52'] = (a0, a1) => (Module['_mono_wasm_f64_to_i52'] = wasmExports['mono_wasm_f64_to_i52'])(a0, a1);
    Module['_mono_wasm_method_get_full_name'] = (a0) => (Module['_mono_wasm_method_get_full_name'] = wasmExports['mono_wasm_method_get_full_name'])(a0);
    Module['_mono_wasm_method_get_name'] = (a0) => (Module['_mono_wasm_method_get_name'] = wasmExports['mono_wasm_method_get_name'])(a0);
    Module['_mono_wasm_method_get_name_ex'] = (a0) => (Module['_mono_wasm_method_get_name_ex'] = wasmExports['mono_wasm_method_get_name_ex'])(a0);
    Module['_mono_wasm_get_f32_unaligned'] = (a0) => (Module['_mono_wasm_get_f32_unaligned'] = wasmExports['mono_wasm_get_f32_unaligned'])(a0);
    Module['_mono_wasm_get_f64_unaligned'] = (a0) => (Module['_mono_wasm_get_f64_unaligned'] = wasmExports['mono_wasm_get_f64_unaligned'])(a0);
    Module['_mono_wasm_get_i32_unaligned'] = (a0) => (Module['_mono_wasm_get_i32_unaligned'] = wasmExports['mono_wasm_get_i32_unaligned'])(a0);
    Module['_mono_wasm_is_zero_page_reserved'] = () => (Module['_mono_wasm_is_zero_page_reserved'] = wasmExports['mono_wasm_is_zero_page_reserved'])();
    Module['_mono_wasm_read_as_bool_or_null_unsafe'] = (a0) => (Module['_mono_wasm_read_as_bool_or_null_unsafe'] = wasmExports['mono_wasm_read_as_bool_or_null_unsafe'])(a0);
    Module['_mono_wasm_assembly_load'] = (a0) => (Module['_mono_wasm_assembly_load'] = wasmExports['mono_wasm_assembly_load'])(a0);
    Module['_mono_wasm_assembly_find_class'] = (a0, a1, a2) => (Module['_mono_wasm_assembly_find_class'] = wasmExports['mono_wasm_assembly_find_class'])(a0, a1, a2);
    Module['_mono_wasm_assembly_find_method'] = (a0, a1, a2) => (Module['_mono_wasm_assembly_find_method'] = wasmExports['mono_wasm_assembly_find_method'])(a0, a1, a2);
    Module['_mono_wasm_send_dbg_command_with_parms'] = (a0, a1, a2, a3, a4, a5, a6) => (Module['_mono_wasm_send_dbg_command_with_parms'] = wasmExports['mono_wasm_send_dbg_command_with_parms'])(a0, a1, a2, a3, a4, a5, a6);
    Module['_mono_wasm_send_dbg_command'] = (a0, a1, a2, a3, a4) => (Module['_mono_wasm_send_dbg_command'] = wasmExports['mono_wasm_send_dbg_command'])(a0, a1, a2, a3, a4);
    Module['_mono_jiterp_register_jit_call_thunk'] = (a0, a1) => (Module['_mono_jiterp_register_jit_call_thunk'] = wasmExports['mono_jiterp_register_jit_call_thunk'])(a0, a1);
    Module['_mono_jiterp_stackval_to_data'] = (a0, a1, a2) => (Module['_mono_jiterp_stackval_to_data'] = wasmExports['mono_jiterp_stackval_to_data'])(a0, a1, a2);
    Module['_mono_jiterp_stackval_from_data'] = (a0, a1, a2) => (Module['_mono_jiterp_stackval_from_data'] = wasmExports['mono_jiterp_stackval_from_data'])(a0, a1, a2);
    Module['_mono_jiterp_get_arg_offset'] = (a0, a1, a2) => (Module['_mono_jiterp_get_arg_offset'] = wasmExports['mono_jiterp_get_arg_offset'])(a0, a1, a2);
    Module['_mono_jiterp_overflow_check_i4'] = (a0, a1, a2) => (Module['_mono_jiterp_overflow_check_i4'] = wasmExports['mono_jiterp_overflow_check_i4'])(a0, a1, a2);
    Module['_mono_jiterp_overflow_check_u4'] = (a0, a1, a2) => (Module['_mono_jiterp_overflow_check_u4'] = wasmExports['mono_jiterp_overflow_check_u4'])(a0, a1, a2);
    Module['_mono_jiterp_ld_delegate_method_ptr'] = (a0, a1) => (Module['_mono_jiterp_ld_delegate_method_ptr'] = wasmExports['mono_jiterp_ld_delegate_method_ptr'])(a0, a1);
    Module['_mono_jiterp_interp_entry'] = (a0, a1) => (Module['_mono_jiterp_interp_entry'] = wasmExports['mono_jiterp_interp_entry'])(a0, a1);
    Module['_memset'] = (a0, a1, a2) => (Module['_memset'] = wasmExports['memset'])(a0, a1, a2);
    Module['_fmodf'] = (a0, a1) => (Module['_fmodf'] = wasmExports['fmodf'])(a0, a1);
    Module['_fmod'] = (a0, a1) => (Module['_fmod'] = wasmExports['fmod'])(a0, a1);
    Module['_asin'] = (a0) => (Module['_asin'] = wasmExports['asin'])(a0);
    Module['_asinh'] = (a0) => (Module['_asinh'] = wasmExports['asinh'])(a0);
    Module['_acos'] = (a0) => (Module['_acos'] = wasmExports['acos'])(a0);
    Module['_acosh'] = (a0) => (Module['_acosh'] = wasmExports['acosh'])(a0);
    Module['_atan'] = (a0) => (Module['_atan'] = wasmExports['atan'])(a0);
    Module['_atanh'] = (a0) => (Module['_atanh'] = wasmExports['atanh'])(a0);
    Module['_cos'] = (a0) => (Module['_cos'] = wasmExports['cos'])(a0);
    Module['_cbrt'] = (a0) => (Module['_cbrt'] = wasmExports['cbrt'])(a0);
    Module['_cosh'] = (a0) => (Module['_cosh'] = wasmExports['cosh'])(a0);
    Module['_exp'] = (a0) => (Module['_exp'] = wasmExports['exp'])(a0);
    Module['_log'] = (a0) => (Module['_log'] = wasmExports['log'])(a0);
    Module['_log2'] = (a0) => (Module['_log2'] = wasmExports['log2'])(a0);
    Module['_log10'] = (a0) => (Module['_log10'] = wasmExports['log10'])(a0);
    Module['_sin'] = (a0) => (Module['_sin'] = wasmExports['sin'])(a0);
    Module['_sinh'] = (a0) => (Module['_sinh'] = wasmExports['sinh'])(a0);
    Module['_tan'] = (a0) => (Module['_tan'] = wasmExports['tan'])(a0);
    Module['_tanh'] = (a0) => (Module['_tanh'] = wasmExports['tanh'])(a0);
    Module['_atan2'] = (a0, a1) => (Module['_atan2'] = wasmExports['atan2'])(a0, a1);
    Module['_pow'] = (a0, a1) => (Module['_pow'] = wasmExports['pow'])(a0, a1);
    Module['_fma'] = (a0, a1, a2) => (Module['_fma'] = wasmExports['fma'])(a0, a1, a2);
    Module['_asinf'] = (a0) => (Module['_asinf'] = wasmExports['asinf'])(a0);
    Module['_asinhf'] = (a0) => (Module['_asinhf'] = wasmExports['asinhf'])(a0);
    Module['_acosf'] = (a0) => (Module['_acosf'] = wasmExports['acosf'])(a0);
    Module['_acoshf'] = (a0) => (Module['_acoshf'] = wasmExports['acoshf'])(a0);
    Module['_atanf'] = (a0) => (Module['_atanf'] = wasmExports['atanf'])(a0);
    Module['_atanhf'] = (a0) => (Module['_atanhf'] = wasmExports['atanhf'])(a0);
    Module['_cosf'] = (a0) => (Module['_cosf'] = wasmExports['cosf'])(a0);
    Module['_cbrtf'] = (a0) => (Module['_cbrtf'] = wasmExports['cbrtf'])(a0);
    Module['_coshf'] = (a0) => (Module['_coshf'] = wasmExports['coshf'])(a0);
    Module['_expf'] = (a0) => (Module['_expf'] = wasmExports['expf'])(a0);
    Module['_logf'] = (a0) => (Module['_logf'] = wasmExports['logf'])(a0);
    Module['_log2f'] = (a0) => (Module['_log2f'] = wasmExports['log2f'])(a0);
    Module['_log10f'] = (a0) => (Module['_log10f'] = wasmExports['log10f'])(a0);
    Module['_sinf'] = (a0) => (Module['_sinf'] = wasmExports['sinf'])(a0);
    Module['_sinhf'] = (a0) => (Module['_sinhf'] = wasmExports['sinhf'])(a0);
    Module['_tanf'] = (a0) => (Module['_tanf'] = wasmExports['tanf'])(a0);
    Module['_tanhf'] = (a0) => (Module['_tanhf'] = wasmExports['tanhf'])(a0);
    Module['_atan2f'] = (a0, a1) => (Module['_atan2f'] = wasmExports['atan2f'])(a0, a1);
    Module['_powf'] = (a0, a1) => (Module['_powf'] = wasmExports['powf'])(a0, a1);
    Module['_fmaf'] = (a0, a1, a2) => (Module['_fmaf'] = wasmExports['fmaf'])(a0, a1, a2);
    Module['_mono_jiterp_get_polling_required_address'] = () => (Module['_mono_jiterp_get_polling_required_address'] = wasmExports['mono_jiterp_get_polling_required_address'])();
    Module['_mono_jiterp_prof_enter'] = (a0, a1) => (Module['_mono_jiterp_prof_enter'] = wasmExports['mono_jiterp_prof_enter'])(a0, a1);
    Module['_mono_jiterp_prof_samplepoint'] = (a0, a1) => (Module['_mono_jiterp_prof_samplepoint'] = wasmExports['mono_jiterp_prof_samplepoint'])(a0, a1);
    Module['_mono_jiterp_prof_leave'] = (a0, a1) => (Module['_mono_jiterp_prof_leave'] = wasmExports['mono_jiterp_prof_leave'])(a0, a1);
    Module['_mono_jiterp_do_safepoint'] = (a0, a1) => (Module['_mono_jiterp_do_safepoint'] = wasmExports['mono_jiterp_do_safepoint'])(a0, a1);
    Module['_mono_jiterp_imethod_to_ftnptr'] = (a0) => (Module['_mono_jiterp_imethod_to_ftnptr'] = wasmExports['mono_jiterp_imethod_to_ftnptr'])(a0);
    Module['_mono_jiterp_enum_hasflag'] = (a0, a1, a2, a3) => (Module['_mono_jiterp_enum_hasflag'] = wasmExports['mono_jiterp_enum_hasflag'])(a0, a1, a2, a3);
    Module['_mono_jiterp_get_simd_intrinsic'] = (a0, a1) => (Module['_mono_jiterp_get_simd_intrinsic'] = wasmExports['mono_jiterp_get_simd_intrinsic'])(a0, a1);
    Module['_mono_jiterp_get_simd_opcode'] = (a0, a1) => (Module['_mono_jiterp_get_simd_opcode'] = wasmExports['mono_jiterp_get_simd_opcode'])(a0, a1);
    Module['_mono_jiterp_get_opcode_info'] = (a0, a1) => (Module['_mono_jiterp_get_opcode_info'] = wasmExports['mono_jiterp_get_opcode_info'])(a0, a1);
    Module['_mono_jiterp_placeholder_trace'] = (a0, a1, a2, a3) => (Module['_mono_jiterp_placeholder_trace'] = wasmExports['mono_jiterp_placeholder_trace'])(a0, a1, a2, a3);
    Module['_mono_jiterp_placeholder_jit_call'] = (a0, a1, a2, a3) => (Module['_mono_jiterp_placeholder_jit_call'] = wasmExports['mono_jiterp_placeholder_jit_call'])(a0, a1, a2, a3);
    Module['_mono_jiterp_get_interp_entry_func'] = (a0) => (Module['_mono_jiterp_get_interp_entry_func'] = wasmExports['mono_jiterp_get_interp_entry_func'])(a0);
    Module['_mono_jiterp_is_enabled'] = () => (Module['_mono_jiterp_is_enabled'] = wasmExports['mono_jiterp_is_enabled'])();
    Module['_mono_jiterp_encode_leb64_ref'] = (a0, a1, a2) => (Module['_mono_jiterp_encode_leb64_ref'] = wasmExports['mono_jiterp_encode_leb64_ref'])(a0, a1, a2);
    Module['_mono_jiterp_encode_leb52'] = (a0, a1, a2) => (Module['_mono_jiterp_encode_leb52'] = wasmExports['mono_jiterp_encode_leb52'])(a0, a1, a2);
    Module['_mono_jiterp_encode_leb_signed_boundary'] = (a0, a1, a2) => (Module['_mono_jiterp_encode_leb_signed_boundary'] = wasmExports['mono_jiterp_encode_leb_signed_boundary'])(a0, a1, a2);
    Module['_mono_jiterp_increase_entry_count'] = (a0) => (Module['_mono_jiterp_increase_entry_count'] = wasmExports['mono_jiterp_increase_entry_count'])(a0);
    Module['_mono_jiterp_object_unbox'] = (a0) => (Module['_mono_jiterp_object_unbox'] = wasmExports['mono_jiterp_object_unbox'])(a0);
    Module['_mono_jiterp_type_is_byref'] = (a0) => (Module['_mono_jiterp_type_is_byref'] = wasmExports['mono_jiterp_type_is_byref'])(a0);
    Module['_mono_jiterp_value_copy'] = (a0, a1, a2) => (Module['_mono_jiterp_value_copy'] = wasmExports['mono_jiterp_value_copy'])(a0, a1, a2);
    Module['_mono_jiterp_try_newobj_inlined'] = (a0, a1) => (Module['_mono_jiterp_try_newobj_inlined'] = wasmExports['mono_jiterp_try_newobj_inlined'])(a0, a1);
    Module['_mono_jiterp_try_newstr'] = (a0, a1) => (Module['_mono_jiterp_try_newstr'] = wasmExports['mono_jiterp_try_newstr'])(a0, a1);
    Module['_mono_jiterp_try_newarr'] = (a0, a1, a2) => (Module['_mono_jiterp_try_newarr'] = wasmExports['mono_jiterp_try_newarr'])(a0, a1, a2);
    Module['_mono_jiterp_gettype_ref'] = (a0, a1) => (Module['_mono_jiterp_gettype_ref'] = wasmExports['mono_jiterp_gettype_ref'])(a0, a1);
    Module['_mono_jiterp_has_parent_fast'] = (a0, a1) => (Module['_mono_jiterp_has_parent_fast'] = wasmExports['mono_jiterp_has_parent_fast'])(a0, a1);
    Module['_mono_jiterp_implements_interface'] = (a0, a1) => (Module['_mono_jiterp_implements_interface'] = wasmExports['mono_jiterp_implements_interface'])(a0, a1);
    Module['_mono_jiterp_is_special_interface'] = (a0) => (Module['_mono_jiterp_is_special_interface'] = wasmExports['mono_jiterp_is_special_interface'])(a0);
    Module['_mono_jiterp_implements_special_interface'] = (a0, a1, a2) => (Module['_mono_jiterp_implements_special_interface'] = wasmExports['mono_jiterp_implements_special_interface'])(a0, a1, a2);
    Module['_mono_jiterp_cast_v2'] = (a0, a1, a2, a3) => (Module['_mono_jiterp_cast_v2'] = wasmExports['mono_jiterp_cast_v2'])(a0, a1, a2, a3);
    Module['_mono_jiterp_localloc'] = (a0, a1, a2) => (Module['_mono_jiterp_localloc'] = wasmExports['mono_jiterp_localloc'])(a0, a1, a2);
    Module['_mono_jiterp_ldtsflda'] = (a0, a1) => (Module['_mono_jiterp_ldtsflda'] = wasmExports['mono_jiterp_ldtsflda'])(a0, a1);
    Module['_mono_jiterp_box_ref'] = (a0, a1, a2, a3) => (Module['_mono_jiterp_box_ref'] = wasmExports['mono_jiterp_box_ref'])(a0, a1, a2, a3);
    Module['_mono_jiterp_conv'] = (a0, a1, a2) => (Module['_mono_jiterp_conv'] = wasmExports['mono_jiterp_conv'])(a0, a1, a2);
    Module['_mono_jiterp_relop_fp'] = (a0, a1, a2) => (Module['_mono_jiterp_relop_fp'] = wasmExports['mono_jiterp_relop_fp'])(a0, a1, a2);
    Module['_mono_jiterp_get_size_of_stackval'] = () => (Module['_mono_jiterp_get_size_of_stackval'] = wasmExports['mono_jiterp_get_size_of_stackval'])();
    Module['_mono_jiterp_type_get_raw_value_size'] = (a0) => (Module['_mono_jiterp_type_get_raw_value_size'] = wasmExports['mono_jiterp_type_get_raw_value_size'])(a0);
    Module['_mono_jiterp_trace_bailout'] = (a0) => (Module['_mono_jiterp_trace_bailout'] = wasmExports['mono_jiterp_trace_bailout'])(a0);
    Module['_mono_jiterp_get_trace_bailout_count'] = (a0) => (Module['_mono_jiterp_get_trace_bailout_count'] = wasmExports['mono_jiterp_get_trace_bailout_count'])(a0);
    Module['_mono_jiterp_adjust_abort_count'] = (a0, a1) => (Module['_mono_jiterp_adjust_abort_count'] = wasmExports['mono_jiterp_adjust_abort_count'])(a0, a1);
    Module['_mono_jiterp_interp_entry_prologue'] = (a0, a1) => (Module['_mono_jiterp_interp_entry_prologue'] = wasmExports['mono_jiterp_interp_entry_prologue'])(a0, a1);
    Module['_mono_jiterp_get_opcode_value_table_entry'] = (a0) => (Module['_mono_jiterp_get_opcode_value_table_entry'] = wasmExports['mono_jiterp_get_opcode_value_table_entry'])(a0);
    Module['_mono_jiterp_get_trace_hit_count'] = (a0) => (Module['_mono_jiterp_get_trace_hit_count'] = wasmExports['mono_jiterp_get_trace_hit_count'])(a0);
    Module['_mono_jiterp_parse_option'] = (a0) => (Module['_mono_jiterp_parse_option'] = wasmExports['mono_jiterp_parse_option'])(a0);
    Module['_mono_jiterp_get_options_version'] = () => (Module['_mono_jiterp_get_options_version'] = wasmExports['mono_jiterp_get_options_version'])();
    Module['_mono_jiterp_get_options_as_json'] = () => (Module['_mono_jiterp_get_options_as_json'] = wasmExports['mono_jiterp_get_options_as_json'])();
    Module['_mono_jiterp_get_option_as_int'] = (a0) => (Module['_mono_jiterp_get_option_as_int'] = wasmExports['mono_jiterp_get_option_as_int'])(a0);
    Module['_mono_jiterp_object_has_component_size'] = (a0) => (Module['_mono_jiterp_object_has_component_size'] = wasmExports['mono_jiterp_object_has_component_size'])(a0);
    Module['_mono_jiterp_get_hashcode'] = (a0) => (Module['_mono_jiterp_get_hashcode'] = wasmExports['mono_jiterp_get_hashcode'])(a0);
    Module['_mono_jiterp_try_get_hashcode'] = (a0) => (Module['_mono_jiterp_try_get_hashcode'] = wasmExports['mono_jiterp_try_get_hashcode'])(a0);
    Module['_mono_jiterp_get_signature_has_this'] = (a0) => (Module['_mono_jiterp_get_signature_has_this'] = wasmExports['mono_jiterp_get_signature_has_this'])(a0);
    Module['_mono_jiterp_get_signature_return_type'] = (a0) => (Module['_mono_jiterp_get_signature_return_type'] = wasmExports['mono_jiterp_get_signature_return_type'])(a0);
    Module['_mono_jiterp_get_signature_param_count'] = (a0) => (Module['_mono_jiterp_get_signature_param_count'] = wasmExports['mono_jiterp_get_signature_param_count'])(a0);
    Module['_mono_jiterp_get_signature_params'] = (a0) => (Module['_mono_jiterp_get_signature_params'] = wasmExports['mono_jiterp_get_signature_params'])(a0);
    Module['_mono_jiterp_type_to_ldind'] = (a0) => (Module['_mono_jiterp_type_to_ldind'] = wasmExports['mono_jiterp_type_to_ldind'])(a0);
    Module['_mono_jiterp_type_to_stind'] = (a0) => (Module['_mono_jiterp_type_to_stind'] = wasmExports['mono_jiterp_type_to_stind'])(a0);
    Module['_mono_jiterp_get_array_rank'] = (a0, a1) => (Module['_mono_jiterp_get_array_rank'] = wasmExports['mono_jiterp_get_array_rank'])(a0, a1);
    Module['_mono_jiterp_get_array_element_size'] = (a0, a1) => (Module['_mono_jiterp_get_array_element_size'] = wasmExports['mono_jiterp_get_array_element_size'])(a0, a1);
    Module['_mono_jiterp_set_object_field'] = (a0, a1, a2, a3) => (Module['_mono_jiterp_set_object_field'] = wasmExports['mono_jiterp_set_object_field'])(a0, a1, a2, a3);
    Module['_mono_jiterp_debug_count'] = () => (Module['_mono_jiterp_debug_count'] = wasmExports['mono_jiterp_debug_count'])();
    Module['_mono_jiterp_stelem_ref'] = (a0, a1, a2) => (Module['_mono_jiterp_stelem_ref'] = wasmExports['mono_jiterp_stelem_ref'])(a0, a1, a2);
    Module['_mono_jiterp_get_member_offset'] = (a0) => (Module['_mono_jiterp_get_member_offset'] = wasmExports['mono_jiterp_get_member_offset'])(a0);
    Module['_mono_jiterp_get_counter'] = (a0) => (Module['_mono_jiterp_get_counter'] = wasmExports['mono_jiterp_get_counter'])(a0);
    Module['_mono_jiterp_modify_counter'] = (a0, a1) => (Module['_mono_jiterp_modify_counter'] = wasmExports['mono_jiterp_modify_counter'])(a0, a1);
    Module['_mono_jiterp_write_number_unaligned'] = (a0, a1, a2) => (Module['_mono_jiterp_write_number_unaligned'] = wasmExports['mono_jiterp_write_number_unaligned'])(a0, a1, a2);
    Module['_mono_jiterp_get_rejected_trace_count'] = () => (Module['_mono_jiterp_get_rejected_trace_count'] = wasmExports['mono_jiterp_get_rejected_trace_count'])();
    Module['_mono_jiterp_boost_back_branch_target'] = (a0) => (Module['_mono_jiterp_boost_back_branch_target'] = wasmExports['mono_jiterp_boost_back_branch_target'])(a0);
    Module['_mono_jiterp_is_imethod_var_address_taken'] = (a0, a1) => (Module['_mono_jiterp_is_imethod_var_address_taken'] = wasmExports['mono_jiterp_is_imethod_var_address_taken'])(a0, a1);
    Module['_mono_jiterp_initialize_table'] = (a0, a1, a2) => (Module['_mono_jiterp_initialize_table'] = wasmExports['mono_jiterp_initialize_table'])(a0, a1, a2);
    Module['_mono_jiterp_allocate_table_entry'] = (a0) => (Module['_mono_jiterp_allocate_table_entry'] = wasmExports['mono_jiterp_allocate_table_entry'])(a0);
    Module['_mono_jiterp_tlqueue_next'] = (a0) => (Module['_mono_jiterp_tlqueue_next'] = wasmExports['mono_jiterp_tlqueue_next'])(a0);
    Module['_mono_jiterp_tlqueue_add'] = (a0, a1) => (Module['_mono_jiterp_tlqueue_add'] = wasmExports['mono_jiterp_tlqueue_add'])(a0, a1);
    Module['_mono_jiterp_tlqueue_clear'] = (a0) => (Module['_mono_jiterp_tlqueue_clear'] = wasmExports['mono_jiterp_tlqueue_clear'])(a0);
    Module['_mono_interp_pgo_load_table'] = (a0, a1) => (Module['_mono_interp_pgo_load_table'] = wasmExports['mono_interp_pgo_load_table'])(a0, a1);
    Module['_mono_interp_pgo_save_table'] = (a0, a1) => (Module['_mono_interp_pgo_save_table'] = wasmExports['mono_interp_pgo_save_table'])(a0, a1);
    Module['_mono_llvm_cpp_catch_exception'] = (a0, a1, a2) => (Module['_mono_llvm_cpp_catch_exception'] = wasmExports['mono_llvm_cpp_catch_exception'])(a0, a1, a2);
    Module['_mono_jiterp_begin_catch'] = (a0) => (Module['_mono_jiterp_begin_catch'] = wasmExports['mono_jiterp_begin_catch'])(a0);
    Module['_mono_jiterp_end_catch'] = () => (Module['_mono_jiterp_end_catch'] = wasmExports['mono_jiterp_end_catch'])();
    Module['_sbrk'] = (a0) => (Module['_sbrk'] = wasmExports['sbrk'])(a0);
    Module['_posix_memalign'] = (a0, a1, a2) => (Module['_posix_memalign'] = wasmExports['posix_memalign'])(a0, a1, a2);
    Module['_mono_background_exec'] = () => (Module['_mono_background_exec'] = wasmExports['mono_background_exec'])();
    Module['_mono_wasm_ds_exec'] = () => (Module['_mono_wasm_ds_exec'] = wasmExports['mono_wasm_ds_exec'])();
    Module['_mono_wasm_gc_lock'] = () => (Module['_mono_wasm_gc_lock'] = wasmExports['mono_wasm_gc_lock'])();
    Module['_mono_wasm_gc_unlock'] = () => (Module['_mono_wasm_gc_unlock'] = wasmExports['mono_wasm_gc_unlock'])();
    Module['_mono_print_method_from_ip'] = (a0) => (Module['_mono_print_method_from_ip'] = wasmExports['mono_print_method_from_ip'])(a0);
    Module['_mono_wasm_execute_timer'] = () => (Module['_mono_wasm_execute_timer'] = wasmExports['mono_wasm_execute_timer'])();
    Module['_mono_wasm_load_icu_data'] = (a0) => (Module['_mono_wasm_load_icu_data'] = wasmExports['mono_wasm_load_icu_data'])(a0);
    var _ntohs = Module['_ntohs'] = (a0) => (_ntohs = Module['_ntohs'] = wasmExports['ntohs'])(a0);
    var _htons = Module['_htons'] = (a0) => (_htons = Module['_htons'] = wasmExports['htons'])(a0);
    var ___funcs_on_exit = () => (___funcs_on_exit = wasmExports['__funcs_on_exit'])();
    var _emscripten_builtin_memalign = (a0, a1) => (_emscripten_builtin_memalign = wasmExports['emscripten_builtin_memalign'])(a0, a1);
    Module['_memalign'] = (a0, a1) => (Module['_memalign'] = wasmExports['memalign'])(a0, a1);
    var ___trap = () => (___trap = wasmExports['__trap'])();
    var stackSave = Module['stackSave'] = () => (stackSave = Module['stackSave'] = wasmExports['stackSave'])();
    var stackRestore = Module['stackRestore'] = (a0) => (stackRestore = Module['stackRestore'] = wasmExports['stackRestore'])(a0);
    var stackAlloc = Module['stackAlloc'] = (a0) => (stackAlloc = Module['stackAlloc'] = wasmExports['stackAlloc'])(a0);


    // include: postamble.js
    // === Auto-generated postamble setup entry stuff ===

    Module['addRunDependency'] = addRunDependency;
    Module['removeRunDependency'] = removeRunDependency;
    Module['FS_createPath'] = FS.createPath;
    Module['FS_createLazyFile'] = FS.createLazyFile;
    Module['FS_createDevice'] = FS.createDevice;
    Module['out'] = out;
    Module['err'] = err;
    Module['abort'] = abort;
    Module['wasmExports'] = wasmExports;
    Module['runtimeKeepalivePush'] = runtimeKeepalivePush;
    Module['runtimeKeepalivePop'] = runtimeKeepalivePop;
    Module['maybeExit'] = maybeExit;
    Module['ccall'] = ccall;
    Module['cwrap'] = cwrap;
    Module['addFunction'] = addFunction;
    Module['setValue'] = setValue;
    Module['getValue'] = getValue;
    Module['UTF8ArrayToString'] = UTF8ArrayToString;
    Module['UTF8ToString'] = UTF8ToString;
    Module['stringToUTF8Array'] = stringToUTF8Array;
    Module['lengthBytesUTF8'] = lengthBytesUTF8;
    Module['safeSetTimeout'] = safeSetTimeout;
    Module['FS_createPreloadedFile'] = FS.createPreloadedFile;
    Module['FS'] = FS;
    Module['FS_createDataFile'] = FS.createDataFile;
    Module['FS_unlink'] = FS.unlink;


    var calledRun;

    dependenciesFulfilled = function runCaller() {
      // If run has never been called, and we should call run (INVOKE_RUN is true, and Module.noInitialRun is not false)
      if (!calledRun) run();
      if (!calledRun) dependenciesFulfilled = runCaller; // try this again later, after new deps are fulfilled
    };

    function run() {

      if (runDependencies > 0) {
        return;
      }

      preRun();

      // a preRun added a dependency, run will be called later
      if (runDependencies > 0) {
        return;
      }

      function doRun() {
        // run may have just been called through dependencies being fulfilled just in this very frame,
        // or while the async setStatus time below was happening
        if (calledRun) return;
        calledRun = true;
        Module['calledRun'] = true;

        if (ABORT) return;

        initRuntime();

        readyPromiseResolve(Module);
        if (Module['onRuntimeInitialized']) Module['onRuntimeInitialized']();

        postRun();
      }

      if (Module['setStatus']) {
        Module['setStatus']('Running...');
        setTimeout(function() {
          setTimeout(function() {
            Module['setStatus']('');
          }, 1);
          doRun();
        }, 1);
      } else
      {
        doRun();
      }
    }

    if (Module['preInit']) {
      if (typeof Module['preInit'] == 'function') Module['preInit'] = [Module['preInit']];
      while (Module['preInit'].length > 0) {
        Module['preInit'].pop()();
      }
    }

    run();

    // end include: postamble.js



      return moduleArg.ready
    }
    );
    })();
    var createDotnetRuntime_default = createDotnetRuntime;
    var fetch = fetch || undefined; var _nativeModuleLoaded = false;

    var dotnet_native_smyhto3fbr_js = /*#__PURE__*/Object.freeze({
        __proto__: null,
        default: createDotnetRuntime_default
    });

    var dotnet_native_miubzbpxmo_wasm = new URL("_framework/dotnet.native.miubzbpxmo.wasm", __spawnjsBundleUrl).href;

    var icudt_CJK_tjcz0u77k5_dat = new URL("_framework/icudt_CJK.tjcz0u77k5.dat", __spawnjsBundleUrl).href;

    var icudt_EFIGS_tptq2av103_dat = new URL("_framework/icudt_EFIGS.tptq2av103.dat", __spawnjsBundleUrl).href;

    var icudt_no_CJK_lfu7j35m59_dat = new URL("_framework/icudt_no_CJK.lfu7j35m59.dat", __spawnjsBundleUrl).href;

    var System_Private_CoreLib_acqhqif0l6_wasm = new URL("_framework/System.Private.CoreLib.acqhqif0l6.wasm", __spawnjsBundleUrl).href;

    var System_Runtime_InteropServices_JavaScript_11d6720mn8_wasm = new URL("_framework/System.Runtime.InteropServices.JavaScript.11d6720mn8.wasm", __spawnjsBundleUrl).href;

    var BouncyCastle_Cryptography_6qnbenkd0k_wasm = new URL("_framework/BouncyCastle.Cryptography.6qnbenkd0k.wasm", __spawnjsBundleUrl).href;

    var Concentus_17t2e6w7vi_wasm = new URL("_framework/Concentus.17t2e6w7vi.wasm", __spawnjsBundleUrl).href;

    var DnsClient_7sic17ztch_wasm = new URL("_framework/DnsClient.7sic17ztch.wasm", __spawnjsBundleUrl).href;

    var ILGPU_Algorithms_2m9vfxwo1w_wasm = new URL("_framework/ILGPU.Algorithms.2m9vfxwo1w.wasm", __spawnjsBundleUrl).href;

    var ILGPU_zzpl2kir08_wasm = new URL("_framework/ILGPU.zzpl2kir08.wasm", __spawnjsBundleUrl).href;

    var Microsoft_AspNetCore_Authorization_4w3z20tuqk_wasm = new URL("_framework/Microsoft.AspNetCore.Authorization.4w3z20tuqk.wasm", __spawnjsBundleUrl).href;

    var Microsoft_AspNetCore_Components_0z8r7oen0q_wasm = new URL("_framework/Microsoft.AspNetCore.Components.0z8r7oen0q.wasm", __spawnjsBundleUrl).href;

    var Microsoft_AspNetCore_Components_Forms_y5xx1fx6r9_wasm = new URL("_framework/Microsoft.AspNetCore.Components.Forms.y5xx1fx6r9.wasm", __spawnjsBundleUrl).href;

    var Microsoft_AspNetCore_Components_Web_aeyv0fw1i0_wasm = new URL("_framework/Microsoft.AspNetCore.Components.Web.aeyv0fw1i0.wasm", __spawnjsBundleUrl).href;

    var Microsoft_AspNetCore_Components_WebAssembly_9qaxkpf6y7_wasm = new URL("_framework/Microsoft.AspNetCore.Components.WebAssembly.9qaxkpf6y7.wasm", __spawnjsBundleUrl).href;

    var Microsoft_AspNetCore_Metadata_s8loo8n9nc_wasm = new URL("_framework/Microsoft.AspNetCore.Metadata.s8loo8n9nc.wasm", __spawnjsBundleUrl).href;

    var Microsoft_CSharp_vc74ka0j2f_wasm = new URL("_framework/Microsoft.CSharp.vc74ka0j2f.wasm", __spawnjsBundleUrl).href;

    var Microsoft_Extensions_Configuration_Abstractions_iqery9nry2_wasm = new URL("_framework/Microsoft.Extensions.Configuration.Abstractions.iqery9nry2.wasm", __spawnjsBundleUrl).href;

    var Microsoft_Extensions_Configuration_Binder_7ebfchnp6m_wasm = new URL("_framework/Microsoft.Extensions.Configuration.Binder.7ebfchnp6m.wasm", __spawnjsBundleUrl).href;

    var Microsoft_Extensions_Configuration_FileExtensions_phra2kqghi_wasm = new URL("_framework/Microsoft.Extensions.Configuration.FileExtensions.phra2kqghi.wasm", __spawnjsBundleUrl).href;

    var Microsoft_Extensions_Configuration_Json_9udmvturlf_wasm = new URL("_framework/Microsoft.Extensions.Configuration.Json.9udmvturlf.wasm", __spawnjsBundleUrl).href;

    var Microsoft_Extensions_Configuration_qjwtydrbeh_wasm = new URL("_framework/Microsoft.Extensions.Configuration.qjwtydrbeh.wasm", __spawnjsBundleUrl).href;

    var Microsoft_Extensions_DependencyInjection_0lh9y2h2sj_wasm = new URL("_framework/Microsoft.Extensions.DependencyInjection.0lh9y2h2sj.wasm", __spawnjsBundleUrl).href;

    var Microsoft_Extensions_DependencyInjection_Abstractions_r4njuum1at_wasm = new URL("_framework/Microsoft.Extensions.DependencyInjection.Abstractions.r4njuum1at.wasm", __spawnjsBundleUrl).href;

    var Microsoft_Extensions_Diagnostics_Abstractions_k7gpa90haf_wasm = new URL("_framework/Microsoft.Extensions.Diagnostics.Abstractions.k7gpa90haf.wasm", __spawnjsBundleUrl).href;

    var Microsoft_Extensions_Diagnostics_e4xkfi1ghr_wasm = new URL("_framework/Microsoft.Extensions.Diagnostics.e4xkfi1ghr.wasm", __spawnjsBundleUrl).href;

    var Microsoft_Extensions_FileProviders_Abstractions_wbhloeatw3_wasm = new URL("_framework/Microsoft.Extensions.FileProviders.Abstractions.wbhloeatw3.wasm", __spawnjsBundleUrl).href;

    var Microsoft_Extensions_FileProviders_Physical_y5k56zvg10_wasm = new URL("_framework/Microsoft.Extensions.FileProviders.Physical.y5k56zvg10.wasm", __spawnjsBundleUrl).href;

    var Microsoft_Extensions_FileSystemGlobbing_4cqycihgg0_wasm = new URL("_framework/Microsoft.Extensions.FileSystemGlobbing.4cqycihgg0.wasm", __spawnjsBundleUrl).href;

    var Microsoft_Extensions_Logging_05o184w673_wasm = new URL("_framework/Microsoft.Extensions.Logging.05o184w673.wasm", __spawnjsBundleUrl).href;

    var Microsoft_Extensions_Logging_Abstractions_ef2lrhidbn_wasm = new URL("_framework/Microsoft.Extensions.Logging.Abstractions.ef2lrhidbn.wasm", __spawnjsBundleUrl).href;

    var Microsoft_Extensions_Options_ConfigurationExtensions_cbvevgdsho_wasm = new URL("_framework/Microsoft.Extensions.Options.ConfigurationExtensions.cbvevgdsho.wasm", __spawnjsBundleUrl).href;

    var Microsoft_Extensions_Options_llivjk1whj_wasm = new URL("_framework/Microsoft.Extensions.Options.llivjk1whj.wasm", __spawnjsBundleUrl).href;

    var Microsoft_Extensions_Primitives_ndur9bx6x9_wasm = new URL("_framework/Microsoft.Extensions.Primitives.ndur9bx6x9.wasm", __spawnjsBundleUrl).href;

    var Microsoft_Extensions_Validation_w1rf8w9vwy_wasm = new URL("_framework/Microsoft.Extensions.Validation.w1rf8w9vwy.wasm", __spawnjsBundleUrl).href;

    var Microsoft_JSInterop_WebAssembly_9udcqny0aq_wasm = new URL("_framework/Microsoft.JSInterop.WebAssembly.9udcqny0aq.wasm", __spawnjsBundleUrl).href;

    var Microsoft_JSInterop_r7i6scgm0q_wasm = new URL("_framework/Microsoft.JSInterop.r7i6scgm0q.wasm", __spawnjsBundleUrl).href;

    var Microsoft_VisualBasic_Core_ic4oks67r5_wasm = new URL("_framework/Microsoft.VisualBasic.Core.ic4oks67r5.wasm", __spawnjsBundleUrl).href;

    var Microsoft_VisualBasic_oawltawhmj_wasm = new URL("_framework/Microsoft.VisualBasic.oawltawhmj.wasm", __spawnjsBundleUrl).href;

    var Microsoft_Win32_Primitives_n6ylnc1rdo_wasm = new URL("_framework/Microsoft.Win32.Primitives.n6ylnc1rdo.wasm", __spawnjsBundleUrl).href;

    var Microsoft_Win32_Registry_205yp9wx9t_wasm = new URL("_framework/Microsoft.Win32.Registry.205yp9wx9t.wasm", __spawnjsBundleUrl).href;

    var SIPSorcery_5325rd4hw5_wasm = new URL("_framework/SIPSorcery.5325rd4hw5.wasm", __spawnjsBundleUrl).href;

    var SIPSorceryMedia_Abstractions_evfg4tvmuu_wasm = new URL("_framework/SIPSorceryMedia.Abstractions.evfg4tvmuu.wasm", __spawnjsBundleUrl).href;

    var SpawnDev_AI_5qmsvo1th1_wasm = new URL("_framework/SpawnDev.AI.5qmsvo1th1.wasm", __spawnjsBundleUrl).href;

    var SpawnDev_AI_Demo_o6pspucbls_wasm = new URL("_framework/SpawnDev.AI.Demo.o6pspucbls.wasm", __spawnjsBundleUrl).href;

    var SpawnDev_AI_Server_3pyra3g39w_wasm = new URL("_framework/SpawnDev.AI.Server.3pyra3g39w.wasm", __spawnjsBundleUrl).href;

    var SpawnDev_AsyncFileSystem_az5z799cdp_wasm = new URL("_framework/SpawnDev.AsyncFileSystem.az5z799cdp.wasm", __spawnjsBundleUrl).href;

    var SpawnDev_BackgroundServices_1yeryblw74_wasm = new URL("_framework/SpawnDev.BackgroundServices.1yeryblw74.wasm", __spawnjsBundleUrl).href;

    var SpawnDev_ILGPU_51q19yjdic_wasm = new URL("_framework/SpawnDev.ILGPU.51q19yjdic.wasm", __spawnjsBundleUrl).href;

    var SpawnDev_ILGPU_ML_ay4mxvr7ji_wasm = new URL("_framework/SpawnDev.ILGPU.ML.ay4mxvr7ji.wasm", __spawnjsBundleUrl).href;

    var SpawnDev_MultiMedia_hz2005i8oz_wasm = new URL("_framework/SpawnDev.MultiMedia.hz2005i8oz.wasm", __spawnjsBundleUrl).href;

    var SpawnDev_Phonemizer_1qvgtwealf_wasm = new URL("_framework/SpawnDev.Phonemizer.1qvgtwealf.wasm", __spawnjsBundleUrl).href;

    var SpawnDev_RTC_j6o696q84t_wasm = new URL("_framework/SpawnDev.RTC.j6o696q84t.wasm", __spawnjsBundleUrl).href;

    var SpawnDev_SpawnJS_42t7ugqy4h_wasm = new URL("_framework/SpawnDev.SpawnJS.42t7ugqy4h.wasm", __spawnjsBundleUrl).href;

    var SpawnDev_SpawnJS_Blazor_35062n6gab_wasm = new URL("_framework/SpawnDev.SpawnJS.Blazor.35062n6gab.wasm", __spawnjsBundleUrl).href;

    var SpawnDev_SpawnJS_Cryptography_u5p38diicb_wasm = new URL("_framework/SpawnDev.SpawnJS.Cryptography.u5p38diicb.wasm", __spawnjsBundleUrl).href;

    var SpawnDev_SpawnJS_RazorRenderer_vtkxzi8wgc_wasm = new URL("_framework/SpawnDev.SpawnJS.RazorRenderer.vtkxzi8wgc.wasm", __spawnjsBundleUrl).href;

    var SpawnDev_SpawnJS_WebWorkers_o4bm440cce_wasm = new URL("_framework/SpawnDev.SpawnJS.WebWorkers.o4bm440cce.wasm", __spawnjsBundleUrl).href;

    var SpawnDev_WebTorrent_715mc8duzo_wasm = new URL("_framework/SpawnDev.WebTorrent.715mc8duzo.wasm", __spawnjsBundleUrl).href;

    var System_AppContext_33lgp2bflw_wasm = new URL("_framework/System.AppContext.33lgp2bflw.wasm", __spawnjsBundleUrl).href;

    var System_Buffers_g7kq07we8q_wasm = new URL("_framework/System.Buffers.g7kq07we8q.wasm", __spawnjsBundleUrl).href;

    var System_Collections_Concurrent_ayt23dc2he_wasm = new URL("_framework/System.Collections.Concurrent.ayt23dc2he.wasm", __spawnjsBundleUrl).href;

    var System_Collections_Immutable_9nsqwhhu4t_wasm = new URL("_framework/System.Collections.Immutable.9nsqwhhu4t.wasm", __spawnjsBundleUrl).href;

    var System_Collections_NonGeneric_525tw7pi7d_wasm = new URL("_framework/System.Collections.NonGeneric.525tw7pi7d.wasm", __spawnjsBundleUrl).href;

    var System_Collections_Specialized_33r4c9ewf6_wasm = new URL("_framework/System.Collections.Specialized.33r4c9ewf6.wasm", __spawnjsBundleUrl).href;

    var System_Collections_gtdrkq93la_wasm = new URL("_framework/System.Collections.gtdrkq93la.wasm", __spawnjsBundleUrl).href;

    var System_ComponentModel_Annotations_sojjklgz5l_wasm = new URL("_framework/System.ComponentModel.Annotations.sojjklgz5l.wasm", __spawnjsBundleUrl).href;

    var System_ComponentModel_DataAnnotations_q8seqyww2r_wasm = new URL("_framework/System.ComponentModel.DataAnnotations.q8seqyww2r.wasm", __spawnjsBundleUrl).href;

    var System_ComponentModel_EventBasedAsync_7b6lvtlm4f_wasm = new URL("_framework/System.ComponentModel.EventBasedAsync.7b6lvtlm4f.wasm", __spawnjsBundleUrl).href;

    var System_ComponentModel_Primitives_nwih63pj9c_wasm = new URL("_framework/System.ComponentModel.Primitives.nwih63pj9c.wasm", __spawnjsBundleUrl).href;

    var System_ComponentModel_TypeConverter_gq1uo9pwbw_wasm = new URL("_framework/System.ComponentModel.TypeConverter.gq1uo9pwbw.wasm", __spawnjsBundleUrl).href;

    var System_ComponentModel_gtzdp6gjij_wasm = new URL("_framework/System.ComponentModel.gtzdp6gjij.wasm", __spawnjsBundleUrl).href;

    var System_Configuration_q733dci1qs_wasm = new URL("_framework/System.Configuration.q733dci1qs.wasm", __spawnjsBundleUrl).href;

    var System_Console_to0mbsovya_wasm = new URL("_framework/System.Console.to0mbsovya.wasm", __spawnjsBundleUrl).href;

    var System_Core_azclj5c5yv_wasm = new URL("_framework/System.Core.azclj5c5yv.wasm", __spawnjsBundleUrl).href;

    var System_Data_Common_nthckp9a3t_wasm = new URL("_framework/System.Data.Common.nthckp9a3t.wasm", __spawnjsBundleUrl).href;

    var System_Data_DataSetExtensions_a9dlaxjhfy_wasm = new URL("_framework/System.Data.DataSetExtensions.a9dlaxjhfy.wasm", __spawnjsBundleUrl).href;

    var System_Data_whqnroy0hk_wasm = new URL("_framework/System.Data.whqnroy0hk.wasm", __spawnjsBundleUrl).href;

    var System_Diagnostics_Contracts_1ocyilnve1_wasm = new URL("_framework/System.Diagnostics.Contracts.1ocyilnve1.wasm", __spawnjsBundleUrl).href;

    var System_Diagnostics_Debug_0akkuosl4u_wasm = new URL("_framework/System.Diagnostics.Debug.0akkuosl4u.wasm", __spawnjsBundleUrl).href;

    var System_Diagnostics_DiagnosticSource_us3v4sw3re_wasm = new URL("_framework/System.Diagnostics.DiagnosticSource.us3v4sw3re.wasm", __spawnjsBundleUrl).href;

    var System_Diagnostics_FileVersionInfo_52zf5lzwq0_wasm = new URL("_framework/System.Diagnostics.FileVersionInfo.52zf5lzwq0.wasm", __spawnjsBundleUrl).href;

    var System_Diagnostics_Process_aksc3z10sf_wasm = new URL("_framework/System.Diagnostics.Process.aksc3z10sf.wasm", __spawnjsBundleUrl).href;

    var System_Diagnostics_StackTrace_oyhnn763kn_wasm = new URL("_framework/System.Diagnostics.StackTrace.oyhnn763kn.wasm", __spawnjsBundleUrl).href;

    var System_Diagnostics_TextWriterTraceListener_4dqf2t866y_wasm = new URL("_framework/System.Diagnostics.TextWriterTraceListener.4dqf2t866y.wasm", __spawnjsBundleUrl).href;

    var System_Diagnostics_Tools_4jmuc696pu_wasm = new URL("_framework/System.Diagnostics.Tools.4jmuc696pu.wasm", __spawnjsBundleUrl).href;

    var System_Diagnostics_TraceSource_4djwg1x5dk_wasm = new URL("_framework/System.Diagnostics.TraceSource.4djwg1x5dk.wasm", __spawnjsBundleUrl).href;

    var System_Diagnostics_Tracing_6xw2h3mehq_wasm = new URL("_framework/System.Diagnostics.Tracing.6xw2h3mehq.wasm", __spawnjsBundleUrl).href;

    var System_Drawing_Primitives_fht3cwi9ck_wasm = new URL("_framework/System.Drawing.Primitives.fht3cwi9ck.wasm", __spawnjsBundleUrl).href;

    var System_Drawing_cby5s9a0on_wasm = new URL("_framework/System.Drawing.cby5s9a0on.wasm", __spawnjsBundleUrl).href;

    var System_Dynamic_Runtime_yi4mxor0by_wasm = new URL("_framework/System.Dynamic.Runtime.yi4mxor0by.wasm", __spawnjsBundleUrl).href;

    var System_Formats_Asn1_56jjdsv3d0_wasm = new URL("_framework/System.Formats.Asn1.56jjdsv3d0.wasm", __spawnjsBundleUrl).href;

    var System_Formats_Tar_89bpoq7vrz_wasm = new URL("_framework/System.Formats.Tar.89bpoq7vrz.wasm", __spawnjsBundleUrl).href;

    var System_Globalization_Calendars_rildgn7mzz_wasm = new URL("_framework/System.Globalization.Calendars.rildgn7mzz.wasm", __spawnjsBundleUrl).href;

    var System_Globalization_Extensions_opr37tm2pn_wasm = new URL("_framework/System.Globalization.Extensions.opr37tm2pn.wasm", __spawnjsBundleUrl).href;

    var System_Globalization_moqtkrs3kx_wasm = new URL("_framework/System.Globalization.moqtkrs3kx.wasm", __spawnjsBundleUrl).href;

    var System_IO_Compression_6pdq5v22pd_wasm = new URL("_framework/System.IO.Compression.6pdq5v22pd.wasm", __spawnjsBundleUrl).href;

    var System_IO_Compression_Brotli_7ohel80nih_wasm = new URL("_framework/System.IO.Compression.Brotli.7ohel80nih.wasm", __spawnjsBundleUrl).href;

    var System_IO_Compression_FileSystem_y83oisb571_wasm = new URL("_framework/System.IO.Compression.FileSystem.y83oisb571.wasm", __spawnjsBundleUrl).href;

    var System_IO_Compression_ZipFile_tmxcz8z1z1_wasm = new URL("_framework/System.IO.Compression.ZipFile.tmxcz8z1z1.wasm", __spawnjsBundleUrl).href;

    var System_IO_FileSystem_AccessControl_xzbtcseu3i_wasm = new URL("_framework/System.IO.FileSystem.AccessControl.xzbtcseu3i.wasm", __spawnjsBundleUrl).href;

    var System_IO_FileSystem_DriveInfo_g3te4cd6dq_wasm = new URL("_framework/System.IO.FileSystem.DriveInfo.g3te4cd6dq.wasm", __spawnjsBundleUrl).href;

    var System_IO_FileSystem_Primitives_s8z1h6e7bs_wasm = new URL("_framework/System.IO.FileSystem.Primitives.s8z1h6e7bs.wasm", __spawnjsBundleUrl).href;

    var System_IO_FileSystem_Watcher_jia322rvtg_wasm = new URL("_framework/System.IO.FileSystem.Watcher.jia322rvtg.wasm", __spawnjsBundleUrl).href;

    var System_IO_FileSystem_y4d3mjm0rs_wasm = new URL("_framework/System.IO.FileSystem.y4d3mjm0rs.wasm", __spawnjsBundleUrl).href;

    var System_IO_IsolatedStorage_vucb3c4eed_wasm = new URL("_framework/System.IO.IsolatedStorage.vucb3c4eed.wasm", __spawnjsBundleUrl).href;

    var System_IO_MemoryMappedFiles_slhu4kxyuf_wasm = new URL("_framework/System.IO.MemoryMappedFiles.slhu4kxyuf.wasm", __spawnjsBundleUrl).href;

    var System_IO_Pipelines_d9mh1mhl6a_wasm = new URL("_framework/System.IO.Pipelines.d9mh1mhl6a.wasm", __spawnjsBundleUrl).href;

    var System_IO_Pipes_6axf1j8g7w_wasm = new URL("_framework/System.IO.Pipes.6axf1j8g7w.wasm", __spawnjsBundleUrl).href;

    var System_IO_Pipes_AccessControl_aawhalzgje_wasm = new URL("_framework/System.IO.Pipes.AccessControl.aawhalzgje.wasm", __spawnjsBundleUrl).href;

    var System_IO_UnmanagedMemoryStream_jtjypz1bw3_wasm = new URL("_framework/System.IO.UnmanagedMemoryStream.jtjypz1bw3.wasm", __spawnjsBundleUrl).href;

    var System_IO_u03kcd51u2_wasm = new URL("_framework/System.IO.u03kcd51u2.wasm", __spawnjsBundleUrl).href;

    var System_Linq_AsyncEnumerable_qecnowng39_wasm = new URL("_framework/System.Linq.AsyncEnumerable.qecnowng39.wasm", __spawnjsBundleUrl).href;

    var System_Linq_Expressions_48ueoty928_wasm = new URL("_framework/System.Linq.Expressions.48ueoty928.wasm", __spawnjsBundleUrl).href;

    var System_Linq_Parallel_6f9mb35gje_wasm = new URL("_framework/System.Linq.Parallel.6f9mb35gje.wasm", __spawnjsBundleUrl).href;

    var System_Linq_Queryable_bv9ee87bon_wasm = new URL("_framework/System.Linq.Queryable.bv9ee87bon.wasm", __spawnjsBundleUrl).href;

    var System_Linq_ffyao6dtjo_wasm = new URL("_framework/System.Linq.ffyao6dtjo.wasm", __spawnjsBundleUrl).href;

    var System_Memory_i6uetwkqik_wasm = new URL("_framework/System.Memory.i6uetwkqik.wasm", __spawnjsBundleUrl).href;

    var System_Net_Http_9odm4o2yfw_wasm = new URL("_framework/System.Net.Http.9odm4o2yfw.wasm", __spawnjsBundleUrl).href;

    var System_Net_Http_Json_lkg2pjap28_wasm = new URL("_framework/System.Net.Http.Json.lkg2pjap28.wasm", __spawnjsBundleUrl).href;

    var System_Net_HttpListener_l269ngb85c_wasm = new URL("_framework/System.Net.HttpListener.l269ngb85c.wasm", __spawnjsBundleUrl).href;

    var System_Net_Mail_7s5j7d2u11_wasm = new URL("_framework/System.Net.Mail.7s5j7d2u11.wasm", __spawnjsBundleUrl).href;

    var System_Net_NameResolution_y3a25frvkb_wasm = new URL("_framework/System.Net.NameResolution.y3a25frvkb.wasm", __spawnjsBundleUrl).href;

    var System_Net_NetworkInformation_6hh4xfj68d_wasm = new URL("_framework/System.Net.NetworkInformation.6hh4xfj68d.wasm", __spawnjsBundleUrl).href;

    var System_Net_Ping_blplcg5q7j_wasm = new URL("_framework/System.Net.Ping.blplcg5q7j.wasm", __spawnjsBundleUrl).href;

    var System_Net_Primitives_52l52f7ge8_wasm = new URL("_framework/System.Net.Primitives.52l52f7ge8.wasm", __spawnjsBundleUrl).href;

    var System_Net_Quic_aw952wuprs_wasm = new URL("_framework/System.Net.Quic.aw952wuprs.wasm", __spawnjsBundleUrl).href;

    var System_Net_Requests_k1mlkdyp0u_wasm = new URL("_framework/System.Net.Requests.k1mlkdyp0u.wasm", __spawnjsBundleUrl).href;

    var System_Net_Security_vm6w2umju9_wasm = new URL("_framework/System.Net.Security.vm6w2umju9.wasm", __spawnjsBundleUrl).href;

    var System_Net_ServerSentEvents_adh3kntjlv_wasm = new URL("_framework/System.Net.ServerSentEvents.adh3kntjlv.wasm", __spawnjsBundleUrl).href;

    var System_Net_ServicePoint_v8e4bui0ea_wasm = new URL("_framework/System.Net.ServicePoint.v8e4bui0ea.wasm", __spawnjsBundleUrl).href;

    var System_Net_Sockets_ts6iauk88c_wasm = new URL("_framework/System.Net.Sockets.ts6iauk88c.wasm", __spawnjsBundleUrl).href;

    var System_Net_WebClient_od0uvbnjit_wasm = new URL("_framework/System.Net.WebClient.od0uvbnjit.wasm", __spawnjsBundleUrl).href;

    var System_Net_WebHeaderCollection_qf50eeagye_wasm = new URL("_framework/System.Net.WebHeaderCollection.qf50eeagye.wasm", __spawnjsBundleUrl).href;

    var System_Net_WebProxy_iavyox40gr_wasm = new URL("_framework/System.Net.WebProxy.iavyox40gr.wasm", __spawnjsBundleUrl).href;

    var System_Net_WebSockets_Client_qeq362432l_wasm = new URL("_framework/System.Net.WebSockets.Client.qeq362432l.wasm", __spawnjsBundleUrl).href;

    var System_Net_WebSockets_oek2p1up7o_wasm = new URL("_framework/System.Net.WebSockets.oek2p1up7o.wasm", __spawnjsBundleUrl).href;

    var System_Net_i47gzsagt6_wasm = new URL("_framework/System.Net.i47gzsagt6.wasm", __spawnjsBundleUrl).href;

    var System_Numerics_Vectors_n63j92lpq0_wasm = new URL("_framework/System.Numerics.Vectors.n63j92lpq0.wasm", __spawnjsBundleUrl).href;

    var System_Numerics_rkirn4ewf6_wasm = new URL("_framework/System.Numerics.rkirn4ewf6.wasm", __spawnjsBundleUrl).href;

    var System_ObjectModel_i378yy84uu_wasm = new URL("_framework/System.ObjectModel.i378yy84uu.wasm", __spawnjsBundleUrl).href;

    var System_Private_DataContractSerialization_sny9oej0eg_wasm = new URL("_framework/System.Private.DataContractSerialization.sny9oej0eg.wasm", __spawnjsBundleUrl).href;

    var System_Private_Uri_esflyl9tnu_wasm = new URL("_framework/System.Private.Uri.esflyl9tnu.wasm", __spawnjsBundleUrl).href;

    var System_Private_Xml_Linq_memmbl9fkq_wasm = new URL("_framework/System.Private.Xml.Linq.memmbl9fkq.wasm", __spawnjsBundleUrl).href;

    var System_Private_Xml_zw12n4yipy_wasm = new URL("_framework/System.Private.Xml.zw12n4yipy.wasm", __spawnjsBundleUrl).href;

    var System_Reflection_14urzokzzb_wasm = new URL("_framework/System.Reflection.14urzokzzb.wasm", __spawnjsBundleUrl).href;

    var System_Reflection_DispatchProxy_3vrt3hlgt3_wasm = new URL("_framework/System.Reflection.DispatchProxy.3vrt3hlgt3.wasm", __spawnjsBundleUrl).href;

    var System_Reflection_Emit_ILGeneration_ng7j4cz770_wasm = new URL("_framework/System.Reflection.Emit.ILGeneration.ng7j4cz770.wasm", __spawnjsBundleUrl).href;

    var System_Reflection_Emit_Lightweight_v2imlilbpe_wasm = new URL("_framework/System.Reflection.Emit.Lightweight.v2imlilbpe.wasm", __spawnjsBundleUrl).href;

    var System_Reflection_Emit_udsnuk3ij6_wasm = new URL("_framework/System.Reflection.Emit.udsnuk3ij6.wasm", __spawnjsBundleUrl).href;

    var System_Reflection_Extensions_182qm6arqh_wasm = new URL("_framework/System.Reflection.Extensions.182qm6arqh.wasm", __spawnjsBundleUrl).href;

    var System_Reflection_Metadata_5mrm43mmwy_wasm = new URL("_framework/System.Reflection.Metadata.5mrm43mmwy.wasm", __spawnjsBundleUrl).href;

    var System_Reflection_Primitives_2qdwh2ddjb_wasm = new URL("_framework/System.Reflection.Primitives.2qdwh2ddjb.wasm", __spawnjsBundleUrl).href;

    var System_Reflection_TypeExtensions_ohzfghpj7u_wasm = new URL("_framework/System.Reflection.TypeExtensions.ohzfghpj7u.wasm", __spawnjsBundleUrl).href;

    var System_Resources_Reader_o5l4jqwkcn_wasm = new URL("_framework/System.Resources.Reader.o5l4jqwkcn.wasm", __spawnjsBundleUrl).href;

    var System_Resources_ResourceManager_u0kj92um25_wasm = new URL("_framework/System.Resources.ResourceManager.u0kj92um25.wasm", __spawnjsBundleUrl).href;

    var System_Resources_Writer_fybhus075b_wasm = new URL("_framework/System.Resources.Writer.fybhus075b.wasm", __spawnjsBundleUrl).href;

    var System_Runtime_CompilerServices_Unsafe_a2v8uztr6q_wasm = new URL("_framework/System.Runtime.CompilerServices.Unsafe.a2v8uztr6q.wasm", __spawnjsBundleUrl).href;

    var System_Runtime_CompilerServices_VisualC_h9ibm7eot2_wasm = new URL("_framework/System.Runtime.CompilerServices.VisualC.h9ibm7eot2.wasm", __spawnjsBundleUrl).href;

    var System_Runtime_Extensions_oueezzziy7_wasm = new URL("_framework/System.Runtime.Extensions.oueezzziy7.wasm", __spawnjsBundleUrl).href;

    var System_Runtime_Handles_pegxo55rww_wasm = new URL("_framework/System.Runtime.Handles.pegxo55rww.wasm", __spawnjsBundleUrl).href;

    var System_Runtime_InteropServices_76dk94nxht_wasm = new URL("_framework/System.Runtime.InteropServices.76dk94nxht.wasm", __spawnjsBundleUrl).href;

    var System_Runtime_InteropServices_RuntimeInformation_himsthc3a6_wasm = new URL("_framework/System.Runtime.InteropServices.RuntimeInformation.himsthc3a6.wasm", __spawnjsBundleUrl).href;

    var System_Runtime_Intrinsics_pxgyd8cnpl_wasm = new URL("_framework/System.Runtime.Intrinsics.pxgyd8cnpl.wasm", __spawnjsBundleUrl).href;

    var System_Runtime_Loader_smrevcdjv4_wasm = new URL("_framework/System.Runtime.Loader.smrevcdjv4.wasm", __spawnjsBundleUrl).href;

    var System_Runtime_Numerics_c79r80p0xa_wasm = new URL("_framework/System.Runtime.Numerics.c79r80p0xa.wasm", __spawnjsBundleUrl).href;

    var System_Runtime_Serialization_Formatters_5nvzlh22y8_wasm = new URL("_framework/System.Runtime.Serialization.Formatters.5nvzlh22y8.wasm", __spawnjsBundleUrl).href;

    var System_Runtime_Serialization_Json_4u1byp8ddu_wasm = new URL("_framework/System.Runtime.Serialization.Json.4u1byp8ddu.wasm", __spawnjsBundleUrl).href;

    var System_Runtime_Serialization_Primitives_0677mizgg8_wasm = new URL("_framework/System.Runtime.Serialization.Primitives.0677mizgg8.wasm", __spawnjsBundleUrl).href;

    var System_Runtime_Serialization_Xml_7t5vv8ymb3_wasm = new URL("_framework/System.Runtime.Serialization.Xml.7t5vv8ymb3.wasm", __spawnjsBundleUrl).href;

    var System_Runtime_Serialization_xyb15dfsu9_wasm = new URL("_framework/System.Runtime.Serialization.xyb15dfsu9.wasm", __spawnjsBundleUrl).href;

    var System_Runtime_p5wq7tenlq_wasm = new URL("_framework/System.Runtime.p5wq7tenlq.wasm", __spawnjsBundleUrl).href;

    var System_Security_AccessControl_9gsd8k6c5m_wasm = new URL("_framework/System.Security.AccessControl.9gsd8k6c5m.wasm", __spawnjsBundleUrl).href;

    var System_Security_Claims_l0ljqfaxwo_wasm = new URL("_framework/System.Security.Claims.l0ljqfaxwo.wasm", __spawnjsBundleUrl).href;

    var System_Security_Cryptography_2ufhqnonzh_wasm = new URL("_framework/System.Security.Cryptography.2ufhqnonzh.wasm", __spawnjsBundleUrl).href;

    var System_Security_Cryptography_Algorithms_i0xv6lwo7z_wasm = new URL("_framework/System.Security.Cryptography.Algorithms.i0xv6lwo7z.wasm", __spawnjsBundleUrl).href;

    var System_Security_Cryptography_Cng_eeehrv0scy_wasm = new URL("_framework/System.Security.Cryptography.Cng.eeehrv0scy.wasm", __spawnjsBundleUrl).href;

    var System_Security_Cryptography_Csp_2c9gox8ebd_wasm = new URL("_framework/System.Security.Cryptography.Csp.2c9gox8ebd.wasm", __spawnjsBundleUrl).href;

    var System_Security_Cryptography_Encoding_axny8s8hqs_wasm = new URL("_framework/System.Security.Cryptography.Encoding.axny8s8hqs.wasm", __spawnjsBundleUrl).href;

    var System_Security_Cryptography_OpenSsl_1sxm4ayhhy_wasm = new URL("_framework/System.Security.Cryptography.OpenSsl.1sxm4ayhhy.wasm", __spawnjsBundleUrl).href;

    var System_Security_Cryptography_Primitives_4ri0a9h1v4_wasm = new URL("_framework/System.Security.Cryptography.Primitives.4ri0a9h1v4.wasm", __spawnjsBundleUrl).href;

    var System_Security_Cryptography_X509Certificates_rncai5fzz3_wasm = new URL("_framework/System.Security.Cryptography.X509Certificates.rncai5fzz3.wasm", __spawnjsBundleUrl).href;

    var System_Security_Principal_1tjw5xr7f1_wasm = new URL("_framework/System.Security.Principal.1tjw5xr7f1.wasm", __spawnjsBundleUrl).href;

    var System_Security_Principal_Windows_9gaajfy1mm_wasm = new URL("_framework/System.Security.Principal.Windows.9gaajfy1mm.wasm", __spawnjsBundleUrl).href;

    var System_Security_SecureString_hbgjudeeg7_wasm = new URL("_framework/System.Security.SecureString.hbgjudeeg7.wasm", __spawnjsBundleUrl).href;

    var System_Security_ua0hvadql2_wasm = new URL("_framework/System.Security.ua0hvadql2.wasm", __spawnjsBundleUrl).href;

    var System_ServiceModel_Web_a5nn12k2du_wasm = new URL("_framework/System.ServiceModel.Web.a5nn12k2du.wasm", __spawnjsBundleUrl).href;

    var System_ServiceProcess_mzhiaa0o9m_wasm = new URL("_framework/System.ServiceProcess.mzhiaa0o9m.wasm", __spawnjsBundleUrl).href;

    var System_Text_Encoding_CodePages_fso8gniga6_wasm = new URL("_framework/System.Text.Encoding.CodePages.fso8gniga6.wasm", __spawnjsBundleUrl).href;

    var System_Text_Encoding_Extensions_jth4fv0ujc_wasm = new URL("_framework/System.Text.Encoding.Extensions.jth4fv0ujc.wasm", __spawnjsBundleUrl).href;

    var System_Text_Encoding_hen1lijr2s_wasm = new URL("_framework/System.Text.Encoding.hen1lijr2s.wasm", __spawnjsBundleUrl).href;

    var System_Text_Encodings_Web_s63xwzfs6e_wasm = new URL("_framework/System.Text.Encodings.Web.s63xwzfs6e.wasm", __spawnjsBundleUrl).href;

    var System_Text_Json_e68ojdl57q_wasm = new URL("_framework/System.Text.Json.e68ojdl57q.wasm", __spawnjsBundleUrl).href;

    var System_Text_RegularExpressions_o5g45g5kfe_wasm = new URL("_framework/System.Text.RegularExpressions.o5g45g5kfe.wasm", __spawnjsBundleUrl).href;

    var System_Threading_AccessControl_vdpdtl3x6u_wasm = new URL("_framework/System.Threading.AccessControl.vdpdtl3x6u.wasm", __spawnjsBundleUrl).href;

    var System_Threading_Channels_wty3mw4oue_wasm = new URL("_framework/System.Threading.Channels.wty3mw4oue.wasm", __spawnjsBundleUrl).href;

    var System_Threading_Overlapped_03j0f78xbg_wasm = new URL("_framework/System.Threading.Overlapped.03j0f78xbg.wasm", __spawnjsBundleUrl).href;

    var System_Threading_Tasks_Dataflow_1qphddued4_wasm = new URL("_framework/System.Threading.Tasks.Dataflow.1qphddued4.wasm", __spawnjsBundleUrl).href;

    var System_Threading_Tasks_Extensions_a6m6fd88nw_wasm = new URL("_framework/System.Threading.Tasks.Extensions.a6m6fd88nw.wasm", __spawnjsBundleUrl).href;

    var System_Threading_Tasks_Parallel_hwicu9gnb7_wasm = new URL("_framework/System.Threading.Tasks.Parallel.hwicu9gnb7.wasm", __spawnjsBundleUrl).href;

    var System_Threading_Tasks_ko7yntasn6_wasm = new URL("_framework/System.Threading.Tasks.ko7yntasn6.wasm", __spawnjsBundleUrl).href;

    var System_Threading_Thread_ydz2qayyqe_wasm = new URL("_framework/System.Threading.Thread.ydz2qayyqe.wasm", __spawnjsBundleUrl).href;

    var System_Threading_ThreadPool_4f7y4725hj_wasm = new URL("_framework/System.Threading.ThreadPool.4f7y4725hj.wasm", __spawnjsBundleUrl).href;

    var System_Threading_Timer_23ma43391f_wasm = new URL("_framework/System.Threading.Timer.23ma43391f.wasm", __spawnjsBundleUrl).href;

    var System_Threading_xg0tuqpte9_wasm = new URL("_framework/System.Threading.xg0tuqpte9.wasm", __spawnjsBundleUrl).href;

    var System_Transactions_1k679dq1nh_wasm = new URL("_framework/System.Transactions.1k679dq1nh.wasm", __spawnjsBundleUrl).href;

    var System_Transactions_Local_vhgy89wfp9_wasm = new URL("_framework/System.Transactions.Local.vhgy89wfp9.wasm", __spawnjsBundleUrl).href;

    var System_ValueTuple_gm53bht7l7_wasm = new URL("_framework/System.ValueTuple.gm53bht7l7.wasm", __spawnjsBundleUrl).href;

    var System_Web_HttpUtility_xg7fizhwzt_wasm = new URL("_framework/System.Web.HttpUtility.xg7fizhwzt.wasm", __spawnjsBundleUrl).href;

    var System_Web_sakoricpi1_wasm = new URL("_framework/System.Web.sakoricpi1.wasm", __spawnjsBundleUrl).href;

    var System_Windows_98z5iu2ekq_wasm = new URL("_framework/System.Windows.98z5iu2ekq.wasm", __spawnjsBundleUrl).href;

    var System_Xml_Linq_4w1i83kr6d_wasm = new URL("_framework/System.Xml.Linq.4w1i83kr6d.wasm", __spawnjsBundleUrl).href;

    var System_Xml_ReaderWriter_gt2wkfrpd2_wasm = new URL("_framework/System.Xml.ReaderWriter.gt2wkfrpd2.wasm", __spawnjsBundleUrl).href;

    var System_Xml_Serialization_ymsdqvtcj5_wasm = new URL("_framework/System.Xml.Serialization.ymsdqvtcj5.wasm", __spawnjsBundleUrl).href;

    var System_Xml_XDocument_nthxjrhml8_wasm = new URL("_framework/System.Xml.XDocument.nthxjrhml8.wasm", __spawnjsBundleUrl).href;

    var System_Xml_XPath_XDocument_64udcjx5rc_wasm = new URL("_framework/System.Xml.XPath.XDocument.64udcjx5rc.wasm", __spawnjsBundleUrl).href;

    var System_Xml_XPath_m6ukudree9_wasm = new URL("_framework/System.Xml.XPath.m6ukudree9.wasm", __spawnjsBundleUrl).href;

    var System_Xml_XmlDocument_uqap183j3j_wasm = new URL("_framework/System.Xml.XmlDocument.uqap183j3j.wasm", __spawnjsBundleUrl).href;

    var System_Xml_XmlSerializer_kyxgnik2yp_wasm = new URL("_framework/System.Xml.XmlSerializer.kyxgnik2yp.wasm", __spawnjsBundleUrl).href;

    var System_Xml_gccv71ojjn_wasm = new URL("_framework/System.Xml.gccv71ojjn.wasm", __spawnjsBundleUrl).href;

    var System_q2zv1ajo7g_wasm = new URL("_framework/System.q2zv1ajo7g.wasm", __spawnjsBundleUrl).href;

    var WindowsBase_nd3r6urbkm_wasm = new URL("_framework/WindowsBase.nd3r6urbkm.wasm", __spawnjsBundleUrl).href;

    var mscorlib_l31p083pc1_wasm = new URL("_framework/mscorlib.l31p083pc1.wasm", __spawnjsBundleUrl).href;

    var netstandard_8ql06rx5ae_wasm = new URL("_framework/netstandard.8ql06rx5ae.wasm", __spawnjsBundleUrl).href;

    var websocket_sharp_hxolgqcoj5_wasm = new URL("_framework/websocket-sharp.hxolgqcoj5.wasm", __spawnjsBundleUrl).href;

    function getDefaultExportFromCjs (x) {
    	return x && x.__esModule && Object.prototype.hasOwnProperty.call(x, 'default') ? x['default'] : x;
    }

    var SpawnDev_SpawnJS_9pjx9wsgr4_lib_module$1 = {};

    var hasRequiredSpawnDev_SpawnJS_9pjx9wsgr4_lib_module;

    function requireSpawnDev_SpawnJS_9pjx9wsgr4_lib_module () {
    	if (hasRequiredSpawnDev_SpawnJS_9pjx9wsgr4_lib_module) return SpawnDev_SpawnJS_9pjx9wsgr4_lib_module$1;
    	hasRequiredSpawnDev_SpawnJS_9pjx9wsgr4_lib_module = 1;

    	// SpawnJSInterop - the Javascript half of SpawnJS.
    	//
    	// Architecture: every JS value that .Net needs to reference is kept in an id-keyed table
    	// (spawnJSObjects) and addressed from .Net by that numeric id. .Net never receives a live JS object
    	// handle (no Microsoft JSObject), only ids and primitives - which is what lets the .Net side avoid
    	// JSObject and its disposal quirk entirely. .Net "holds" a value (spawnJSObjectHold -> id) and later
    	// "releases" it (spawnJSObjectRelease) so it can be garbage-collected.
    	//
    	// Negative ids are reserved sentinels resolved without a table lookup:
    	//   -1 globalThis, -2 undefined, -3 null, -4 the spawnJSObjects table, SpawnJSInterop.
    	//
    	// Calls flow through _spawnJSInteropCall (sync) / _spawnJSInteropCallAsync (async): .Net names a static
    	// method here plus a returnType code (see ReturnType.cs), and _serializeToNet shapes the result to match.
    	(function () {
    	    if (globalThis.SpawnJSInterop) return;
    	    class SpawnJSInterop {
    	        // enables verbose logging
    	        static verbose = globalThis.spawnJSInteropVerbose;
    	        // ArrayBufferView constructors
    	        static HeapViewCtors = [
    	            globalThis.BigInt64Array,                     // 0: BigInt64Array
    	            globalThis.BigUint64Array,                    // 1: BigUInt64Array
    	            globalThis.Float16Array,                      // 2: Float16Array
    	            globalThis.Float32Array,                      // 3: Float32Array
    	            globalThis.Float64Array,                      // 4: Float64Array
    	            globalThis.Int16Array,                        // 5: Int16Array
    	            globalThis.Int32Array,                        // 6: Int32Array
    	            globalThis.Int8Array,                         // 7: Int8Array
    	            globalThis.Uint16Array,                       // 8: Uint16Array
    	            globalThis.Uint32Array,                       // 9: Uint32Array
    	            globalThis.Uint8Array,                        // 10: Uint8Array
    	            globalThis.Uint8ClampedArray,                 // 11: Uint8ClampedArray
    	            globalThis.DataView                           // 12: DataView
    	        ];
    	        static _revivers = [];
    	        static _replacers = [];
    	        // method names for index based calling as an alternative to string
    	        static _methodMapNames = [];
    	        // methods mapped by index for index based calling as an alternative to string
    	        static _methodMap = [];
    	        // The id -> JS value table. Holds every value .Net currently references.
    	        static spawnJSObjects = {};
    	        // Monotonic id source; never reused, so a stale .Net id can never collide with a live value.
    	        static _sjsObjectIdNext = 0;
    	        // .Net Wasm app instance infos
    	        static _instances = {};
    	        // .Net callbacks
    	        static _callbacks = {};
    	        static _detachedSentinelId = null;
    	        // static constructor
    	        static {
    	            // SpawnJSInterop.registerReviver('__reviverTest', (key, value) => {
    	            //     if (SpawnJSInterop.verbose) console.log('[SpawnJSInterop] reviver test', key, value);
    	            //     return value;
    	            // });
    	            // SpawnJSInterop.registerReplacer('__replacerTest', (key, value) => {
    	            //     if (SpawnJSInterop.verbose) console.log('[SpawnJSInterop] replacer test', key, value);
    	            //     return value;
    	            // });
    	            SpawnJSInterop.refreshMethodMap();
    	            // detachedHeap sentinel
    	            this._detachedSentinelId = setInterval(() => SpawnJSInterop.detachedEventCheck(), 500);
    	        }

    	        // JSImports
    	        // Methods accessed via JSImport only send and recieve basic
    	        // data types and are used to support the marshalling framework
    	        // that allows working with much more advanced data types.

    	        // JSImport
    	        // double _registerInstance(
    	        //   JSObject dotnetInstance,
    	        //   [JSMarshalAs<JSType.Function>] Action onMethodAdded,
    	        //   [JSMarshalAs<JSType.Function<JSType.Number, JSType.String>>] Action<double, string> onAsyncResolvedVoid,
    	        //   [JSMarshalAs<JSType.Function<JSType.Number, JSType.Number, JSType.String>>] Action<double, double, string> onAsyncResolvedDouble,
    	        //   [JSMarshalAs<JSType.Function<JSType.Number, JSType.Boolean, JSType.String>>] Action<double, bool, string> onAsyncResolvedBool,
    	        //   [JSMarshalAs<JSType.Function<JSType.Number, JSType.String, JSType.String>>] Action<double, string, string> onAsyncResolvedString,
    	        //   [JSMarshalAs<JSType.Function<JSType.Number, JSType.Any, JSType.String>>] Action<double, object, string> onAsyncResolvedDoubleNullable,
    	        //   [JSMarshalAs<JSType.Function<JSType.Number, JSType.Any, JSType.String>>] Action<double, object, string> onAsyncResolvedBooleanNullable,
    	        //   [JSMarshalAs<JSType.Function<JSType.Number, JSType.Number, JSType.String>>] Action<double, int, string> onAsyncResolvedInt32,
    	        //   [JSMarshalAs<JSType.Function<JSType.Number, JSType.Any, JSType.String>>] Action<double, object, string> onAsyncResolvedInt32Nullable,
    	        //   [JSMarshalAs<JSType.Function<JSType.Number, JSType.Number>>] Action<long, long> onDetachedHeap,
    	        //   [JSMarshalAs<JSType.Function<JSType.Number, JSType.Number, JSType.Number>>] Action<double, double, double> onCallback)
    	        static _registerInstance(
    	            dotnet,
    	            onMethodAdded,
    	            resolveVoid,
    	            resolveDouble,
    	            resolveBoolean,
    	            resolveString,
    	            resolveDoubleNullable,
    	            resolveBooleanNullable,
    	            resolveInt32,
    	            resolveInt32Nullable,
    	            onDetachedHeap,
    	            handleCallback
    	            ) {
    	            if (!dotnet) throw new Error('dotnet not set');
    	            var instanceInfo = SpawnJSInterop._getInstaceFromDotNet(dotnet);
    	            if (instanceInfo) return instanceInfo.dotnetId;

    	            var dotnetId = SpawnJSInterop.spawnJSObjectHold(dotnet);

    	            // attach the heap buffer to instanceInfo so it can be monitored for detach events
    	            var heapBuffer = SpawnJSInterop.wasmMemoryBuffer(dotnet);

    	            var instanceInfo = { heapBuffer, dotnet, dotnetId, onMethodAdded, resolveVoid, resolveDouble, resolveBoolean, resolveString, resolveDoubleNullable, resolveBooleanNullable, resolveInt32, resolveInt32Nullable, handleCallback, onDetachedHeap };
    	            SpawnJSInterop._instances[dotnetId] = instanceInfo;

    	            instanceInfo.heapBufferSize = heapBuffer.byteLength;
    	            instanceInfo.getHeap = () => {
    	                if (instanceInfo.heapBuffer.detached) {
    	                    if (SpawnJSInterop.verbose) console.log(`Detached heap detected for: ${dotnetId}`);
    	                    // get the new heap buffer
    	                    instanceInfo.heapBuffer = SpawnJSInterop.wasmMemoryBuffer(dotnet);
    	                    const oldSize = instanceInfo.heapBufferSize;
    	                    instanceInfo.heapBufferSize = instanceInfo.heapBuffer.byteLength;
    	                    // notify the instance
    	                    instanceInfo.onDetachedHeap(oldSize, instanceInfo.heapBuffer.byteLength);
    	                }
    	                return instanceInfo.heapBuffer;
    	            };

    	            if (SpawnJSInterop.verbose) console.log('[SpawnJSInterop] Instance registered', instanceInfo);
    	            return dotnetId;
    	        }
    	        // Main .Net to JS entrypoint (synchronous)
    	        // The args array is fetched AND replaced with a fresh empty [] in the same slot, so the .Net side's
    	        // pooled argument-array reference (same id) comes back emptied and ready to reuse on the next call.
    	        // JSImport
    	        // bool? _spawnJSInteropCallBooleanNullable(int returnType, int methodIndex, double argsId);
    	        // double? _spawnJSInteropCallDoubleNullable(int returnType, int methodIndex, double argsId);
    	        // bool _spawnJSInteropCallBoolean(int returnType, int methodIndex, double argsId);
    	        // int _spawnJSInteropCallInt32(int returnType, int methodIndex, double argsId);
    	        // int? _spawnJSInteropCallInt32Nullable(int returnType, int methodIndex, double argsId);
    	        // double _spawnJSInteropCallDouble(int returnType, int methodIndex, double argsId);
    	        // string _spawnJSInteropCallString(int returnType, int methodIndex, double argsId);
    	        // void _spawnJSInteropCallVoid(int returnType, int methodIndex, double argsId);
    	        static _spawnJSInteropCall(returnType, methodName, argsId, replacerId, replacerConfig) {
    	            SpawnJSInterop.detachedEventCheck();
    	            var target = typeof methodName === 'string' ? SpawnJSInterop[methodName] : SpawnJSInterop._methodMap[methodName];
    	            var args = argsId === null || argsId === undefined ? null : SpawnJSInterop.spawnJSObjectGetAndReplace(argsId, []);
    	            // iterate _revivers to process args if needed
    	            if (args) {
    	                for (let i = 0; i < args.length; i++) {
    	                    args[i] = SpawnJSInterop.reviveValue(i, args[i], false);
    	                }
    	            }
    	            var ret = !args ? target() : target(...args);
    	            // run replacers
    	            var replacer = !replacerId ? null : typeof replacerId === 'string' ? SpawnJSInterop[replacerId] : SpawnJSInterop._methodMap[replacerId];
    	            if (replacer) ret = replacer(null, ret, true, replacerConfig);
    	            else ret = SpawnJSInterop.replaceValue(null, ret, false);
    	            // shape the result to match the .Net side's expected returnType
    	            ret = SpawnJSInterop._serializeToNet(returnType, ret);
    	            return ret;
    	        }
    	        // Main .Net to JS entrypoint (asynchronous)
    	        // JSImport
    	        // void _spawnJSInteropCallAsync(int returnType, double dotnetId, double asyncCallId, double methodIndex, double argsId);
    	        static async _spawnJSInteropCallAsync(returnType, dotnetId, asyncCallId, methodName, argsId, replacerId, replacerConfig) {
    	            SpawnJSInterop.detachedEventCheck();
    	            var target = typeof methodName === 'string' ? SpawnJSInterop[methodName] : SpawnJSInterop._methodMap[methodName];
    	            var instance = SpawnJSInterop.getInstace(dotnetId);
    	            SpawnJSInterop.spawnJSObjectGet(dotnetId);
    	            var args = argsId === null || argsId === undefined ? null : SpawnJSInterop.spawnJSObjectGetAndReplace(argsId, []);
    	            if (args) {
    	                for (let i = 0; i < args.length; i++) {
    	                    args[i] = SpawnJSInterop.reviveValue(i, args[i], false);
    	                }
    	            }
    	            var error = null;
    	            var ret = null;
    	            try {
    	                ret = !args ? target() : target(...args);
    	                ret = await ret;
    	                // run replacers
    	                var replacer = !replacerId ? null : typeof replacerId === 'string' ? SpawnJSInterop[replacerId] : SpawnJSInterop._methodMap[replacerId];
    	                if (replacer) ret = replacer(null, ret, true, replacerConfig);
    	                else ret = SpawnJSInterop.replaceValue(null, ret, false);
    	                // prepare using returnType
    	                ret = SpawnJSInterop._serializeToNet(returnType, ret);
    	            } catch (ex) {
    	                // call the exceptionCallbackId
    	                error = SpawnJSInterop.errorToString(ex);
    	                ret = null;
    	            }
    	            switch (returnType) {
    	                case 0: // void
    	                    instance.resolveVoid(asyncCallId, error);
    	                    break;
    	                case 1: // Double
    	                    instance.resolveDouble(asyncCallId, ret, error);
    	                    break;
    	                case 2: // Boolean
    	                    instance.resolveBoolean(asyncCallId, ret, error);
    	                    break;
    	                case 3: // DoubleNullable
    	                    instance.resolveDoubleNullable(asyncCallId, ret, error);
    	                    break;
    	                case 4: // BooleanNullable
    	                    instance.resolveBooleanNullable(asyncCallId, ret, error);
    	                    break;
    	                case 5: // String
    	                    instance.resolveString(asyncCallId, ret, error);
    	                    break;
    	                case 6: // SpawnJSObject
    	                    instance.resolveDouble(asyncCallId, ret, error);
    	                    break;
    	                case 7: // SpawnJSObjectNonNullable
    	                    instance.resolveDouble(asyncCallId, ret, error);
    	                    break;
    	                case 8: // Json
    	                    instance.resolveString(asyncCallId, ret, error);
    	                    break;
    	                case 9: // Int32
    	                    instance.resolveInt32(asyncCallId, ret, error);
    	                    break;
    	                case 10: // Int32Nullable
    	                    instance.resolveInt32Nullable(asyncCallId, ret, error);
    	                    break;
    	                default:
    	                    throw new Error(`Unsupported returnType ${returnType}`);
    	            }
    	        }
    	        // creates a new Array, adds it to the hold and returns it
    	        // JSImport 
    	        // double _spawnJSObjectNewArray();
    	        static spawnJSObjectNewArray() {
    	            return SpawnJSInterop.spawnJSObjectHold([]);
    	        }
    	        // refreshes the method map by looking for any new methods and adds them
    	        // JSImport
    	        // string[] _refreshMethodMap();
    	        static refreshMethodMap() {
    	            var changed = false;
    	            var keys = Reflect.ownKeys(SpawnJSInterop);
    	            for (const pName of keys) {
    	                if (SpawnJSInterop._methodMapNames.indexOf(pName) !== -1) continue;
    	                var propVal = SpawnJSInterop[pName];
    	                if (typeof propVal === 'function') {
    	                    var fn = propVal.bind(SpawnJSInterop);
    	                    changed = true;
    	                    SpawnJSInterop._methodMap.push(fn);
    	                    SpawnJSInterop._methodMapNames.push(pName);
    	                    if (pName.indexOf('__reviver') === 0) {
    	                        SpawnJSInterop._revivers.push(fn);
    	                    } else if (pName.indexOf('__replacer') === 0) {
    	                        SpawnJSInterop._replacers.push(fn);
    	                    }
    	                }
    	            }
    	            if (changed) {
    	                // notify existing instances
    	                for (const dotnetIdExisting in SpawnJSInterop._instances) {
    	                    if (Object.hasOwn(SpawnJSInterop._instances, dotnetIdExisting)) {
    	                        var existingInfo = SpawnJSInterop._instances[dotnetIdExisting];
    	                        try {
    	                            existingInfo.onMethodAdded();
    	                        } catch { }
    	                    }
    	                }
    	            }
    	            return SpawnJSInterop._methodMapNames;
    	        }
    	        // JSImport
    	        // void _releaseCallback(double dotnetId, double callbackId);
    	        static releaseCallback(dotnetId, callbackId) {
    	            var callbackIdPair = `${dotnetId}_${callbackId}`;
    	            delete SpawnJSInterop._callbacks[callbackIdPair];
    	        }
    	        // removes the object from the hold and returns it
    	        // JSImport
    	        // void SpawnJSObjectRelease(double sjsId);
    	        // bool SpawnJSObjectReleaseBoolean(double sjsId);
    	        // double SpawnJSObjectReleaseDouble(double sjsId);
    	        // int SpawnJSObjectReleaseInt32(double sjsId);
    	        // bool? SpawnJSObjectReleaseBooleanNullable(double sjsId);
    	        // int? SpawnJSObjectReleaseInt32Nullable(double sjsId);
    	        // double? SpawnJSObjectReleaseDoubleNullable(double sjsId);
    	        static spawnJSObjectRelease(sjsId) {
    	            var ret = SpawnJSInterop.spawnJSObjects[sjsId];
    	            delete SpawnJSInterop.spawnJSObjects[sjsId];
    	            return ret;
    	        }
    	        // removes the object from the hold, JSON.stringifies it and returns it
    	        // JSImport
    	        // string SpawnJSObjectReleaseJson(double sjsId);
    	        static spawnJSObjectReleaseAsJson(sjsId) {
    	            var ret = SpawnJSInterop.spawnJSObjects[sjsId];
    	            delete SpawnJSInterop.spawnJSObjects[sjsId];
    	            return JSON.stringify(ret);
    	        }
    	        // returns true if the item id exists in the hold
    	        // JSImport
    	        // bool SpawnJSObjectHoldExists(double sjsId);
    	        static spawnJSObjectHoldExists(sjsId) {
    	            return sjsId in SpawnJSInterop.spawnJSObjects;
    	        }
    	        // returns string
    	        // JSImport
    	        // string _getTypeInfo(double sjsId);
    	        static getTypeInfo(sjsId) {
    	            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
    	            var jsClass = Object.prototype.toString.call(obj).split(' ')[1].slice(0, -1);
    	            var jsType = typeof (obj);
    	            return `${jsType} ${jsClass}`;
    	        }

    	        // **************************************************************************************
    	        // SpawnJSObjectReference imports
    	        // Note: only `string key` imports shown. Also exists: `double key`, `int key`
    	        // **************************************************************************************

    	        // property info
    	        // returns string or null if the property was not found
    	        // JSImport
    	        // string _propertyTypeInfo(double sjsId, string key);
    	        static propertyTypeInfo(sjsId, key) {
    	            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
    	            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
    	            if (shortCircuit) return null;
    	            if (!SpawnJSInterop._in(propertyName, parent)) return null;
    	            var value = parent[propertyName];
    	            var jsClass = Object.prototype.toString.call(value).split(' ')[1].slice(0, -1);
    	            var jsType = typeof (value);
    	            return `${jsType} ${jsClass}`;
    	        }
    	        // deletes property
    	        // JSImport
    	        // bool _propertyDelete(double sjsId, string key);
    	        static propertyDelete(sjsId, key) {
    	            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
    	            if (obj === void 0 || obj === null) throw new Error('obj null or undefined');
    	            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
    	            if (shortCircuit) return true;
    	            return delete parent[propertyName];
    	        }
    	        // returns bool if the key is in the target
    	        // JSImport
    	        // bool _propertyIn(double sjsId, string key);
    	        static propertyIn(sjsId, key) {
    	            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
    	            if (obj === void 0 || obj === null) throw new Error('obj null or undefined');
    	            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
    	            if (shortCircuit) return false;
    	            return SpawnJSInterop._in(propertyName, parent);
    	        }
    	        // get a property
    	        // JSImport
    	        // string _propertyGetString(double sjsId, string key);
    	        // double _propertyGetDouble(double sjsId, string key);
    	        // double? _propertyGetDoubleNullable(double sjsId, string key);
    	        // bool _propertyGetBoolean(double sjsId, string key);
    	        // bool? _propertyGetBooleanNullable(double sjsId, string key);
    	        // int _propertyGetInt32(double sjsId, string key);
    	        // int? _propertyGetInt32(double sjsId, string key);
    	        static propertyGet(sjsId, key) {
    	            var ret = undefined;
    	            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
    	            var pathInfo = SpawnJSInterop.pathObjectInfo(obj, key);
    	            if (pathInfo.shortCircuit) return ret;
    	            if (typeof pathInfo.target === 'function') {
    	                ret = pathInfo.target.bind(pathInfo.parent);
    	            } else {
    	                ret = pathInfo.target;
    	            }
    	            return ret;
    	        }
    	        // get a property as a SpawnJSObjectReference
    	        // JSImport
    	        // double _propertyGetSpawnJSObjectReference(double sjsId, string key, bool force);
    	        static propertyGetSpawnJSObjectReference(sjsId, key, force) {
    	            var ret = SpawnJSInterop.propertyGet(sjsId, key);
    	            if (force) {
    	                ret = SpawnJSInterop.spawnJSObjectHold(ret);
    	            } else {
    	                ret = ret === null || ret === undefined ? null : SpawnJSInterop.spawnJSObjectHold(ret);
    	            }
    	            return ret;
    	        }
    	        // get a property as json
    	        // JSImport
    	        // string? _propertyGetJson(double sjsId, string key);
    	        static propertyGetJson(sjsId, key) {
    	            var ret = SpawnJSInterop.propertyGet(sjsId, key);
    	            ret = JSON.stringify(ret);
    	            return ret;
    	        }
    	        // get a property
    	        // JSImport
    	        // string _propertyGetWithReplacerString(double sjsId, string key, double methodIndex);
    	        // double _propertyGetWithReplacerDouble(double sjsId, string key, double methodIndex);
    	        // double? _propertyGetWithReplacerDoubleNullable(double sjsId, string key, double methodIndex);
    	        // bool _propertyGetWithReplacerBoolean(double sjsId, string key, double methodIndex);
    	        // bool? _propertyGetWithReplacerBooleanNullable(double sjsId, string key, double methodIndex);
    	        // string _propertyGetWithReplacerString(double sjsId, string key, double methodIndex, double replacerConfig);
    	        // double _propertyGetWithReplacerDouble(double sjsId, string key, double methodIndex, double replacerConfig);
    	        // double? _propertyGetWithReplacerDoubleNullable(double sjsId, string key, double methodIndex, double replacerConfig);
    	        // bool _propertyGetWithReplacerBoolean(double sjsId, string key, double methodIndex, double replacerConfig);
    	        // bool? _propertyGetWithReplacerBooleanNullable(double sjsId, string key, double methodIndex, double replacerConfig);
    	        // string _propertyGetWithReplacerString(double sjsId, string key, double methodIndex, string replacerConfig);
    	        // double _propertyGetWithReplacerDouble(double sjsId, string key, double methodIndex, string replacerConfig);
    	        // double? _propertyGetWithReplacerDoubleNullable(double sjsId, string key, double methodIndex, string replacerConfig);
    	        // bool _propertyGetWithReplacerBoolean(double sjsId, string key, double methodIndex, string replacerConfig);
    	        // bool? _propertyGetWithReplacerBooleanNullable(double sjsId, string key, double methodIndex, string replacerConfig);
    	        static propertyGetWithReplacer(sjsId, key, methodIndex, replacerConfig) {
    	            var ret = undefined;
    	            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
    	            var pathInfo = SpawnJSInterop.pathObjectInfo(obj, key);
    	            if (pathInfo.shortCircuit) return ret;
    	            if (typeof pathInfo.target === 'function') {
    	                ret = pathInfo.target.bind(pathInfo.parent);
    	            } else {
    	                ret = pathInfo.target;
    	            }
    	            var replacer = typeof methodIndex === 'string' ? SpawnJSInterop[methodIndex] : SpawnJSInterop._methodMap[methodIndex];
    	            if (!replacer) throw new Error(`Reviver not found: ${methodName}`);
    	            ret = !replacer ? ret : replacer(pathInfo.propertyName, ret, true, replacerConfig);
    	            return ret;
    	        }
    	        // set property
    	        // JSImport
    	        // void _propertySet(double sjsId, string key, string value);
    	        // void _propertySet(double sjsId, string key, bool value);
    	        // void _propertySet(double sjsId, string key, double value);
    	        // void _propertySet(double sjsId, string key, int value);
    	        // void _propertySet(double sjsId, string key, bool? value);
    	        // void _propertySet(double sjsId, string key, double? value);
    	        static propertySet(sjsId, key, value) {
    	            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
    	            if (obj === void 0 || obj === null) throw new Error('obj null or undefined');
    	            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
    	            if (shortCircuit) return;
    	            // revivers
    	            value = SpawnJSInterop.reviveValue(propertyName, value, false);
    	            parent[propertyName] = value;
    	        }
    	        // This is where SpawnJSObjectReference revives itself into JS via its sjsId
    	        // set property to a SpawnJSObjectReference
    	        // JSImport
    	        // void _propertySetSpawnJSObject(double sjsId, string key, double value);
    	        static propertySetSpawnJSObject(sjsId, key, valueId) {
    	            var value = SpawnJSInterop.spawnJSObjectGet(valueId);
    	            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
    	            if (obj === void 0 || obj === null) throw new Error('obj null or undefined');
    	            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
    	            if (shortCircuit) return;
    	            parent[propertyName] = value;
    	        }
    	        // set property to a Json
    	        // JSImport
    	        // void _propertySetJson(double sjsId, string key, string value);
    	        static propertySetJson(sjsId, key, json) {
    	            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
    	            if (obj === void 0 || obj === null) throw new Error('obj null or undefined');
    	            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
    	            if (shortCircuit) return;
    	            var value = JSON.parse(json);
    	            parent[propertyName] = value;
    	        }
    	        // set property to a HeapView
    	        // JSImport
    	        // void _propertySetHeapView(double sjsId, string key, double dotnetId, double viewType, double offset, double length, bool copy);
    	        static propertySetHeapView(sjsId, key, dotnetId, viewType, offset, length, copy) {
    	            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
    	            if (obj === void 0 || obj === null) throw new Error('obj null or undefined');
    	            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
    	            if (shortCircuit) return;
    	            // create the heapView meta data
    	            var heapViewInfo = { dotnetId, viewType, offset, length, copy };
    	            // viewType is an INDEX into HeapViewCtors, and index 0 (BigInt64Array) is a real value - a
    	            // falsy check would read it as "missing" and silently rewrite it. The default is the
    	            // Uint8Array index, not its name: this value is used as an index, never as a lookup key.
    	            if (heapViewInfo.viewType === undefined || heapViewInfo.viewType === null) heapViewInfo.viewType = 10;
    	            heapViewInfo.instance = SpawnJSInterop.getInstace(heapViewInfo.dotnetId);
    	            heapViewInfo.dotnet = SpawnJSInterop.spawnJSObjectGet(heapViewInfo.dotnetId);
    	            heapViewInfo.sizeHistory = [];
    	            heapViewInfo.ctor = SpawnJSInterop.getArrayBufferViewConstructor(heapViewInfo.viewType);
    	            // refresh the heapView
    	            var value = SpawnJSInterop.heapViewRefresh(heapViewInfo);
    	            // set to the proeprty
    	            parent[propertyName] = value;
    	        }
    	        // set property null
    	        // JSImport
    	        // void _propertySetNull(double sjsId, string key);
    	        static propertySetNull(sjsId, key) {
    	            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
    	            if (obj === void 0 || obj === null) throw new Error('obj null or undefined');
    	            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
    	            if (shortCircuit) return;
    	            parent[propertyName] = null;
    	        }
    	        // set property undefined
    	        // JSImport
    	        // void _propertySetUndefined(double sjsId, string key);
    	        static propertySetUndefined(sjsId, key) {
    	            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
    	            if (obj === void 0 || obj === null) throw new Error('obj null or undefined');
    	            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
    	            if (shortCircuit) return;
    	            parent[propertyName] = undefined;
    	        }
    	        // JSImport
    	        // void _propertySetCallback(double sjsId, string key, double dotnetId, double callbackId, bool once);
    	        static propertySetCallback(sjsId, key, dotnetId, callbackId, once) {
    	            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
    	            if (obj === void 0 || obj === null) throw new Error('obj null or undefined');
    	            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
    	            if (shortCircuit) return;
    	            if (!callbackId || !dotnetId) {
    	                return null;
    	            }
    	            // get callback id that is globally unique
    	            var callbackIdPair = `${dotnetId}_${callbackId}`;
    	            // check if it exists and createa new function if not
    	            var value = SpawnJSInterop._callbacks[callbackIdPair];
    	            if (!value) {
    	                value = function (...args) {
    	                    // check if the callback has been removed
    	                    if (!SpawnJSInterop._callbacks[callbackIdPair]) {
    	                        return;
    	                    }
    	                    // get the SpawnJSRuntime instance's method that is used to report the callback 
    	                    var { handleCallback } = SpawnJSInterop.getInstace(dotnetId);
    	                    // get argsCnt because after when we call handleCallback .Net will write
    	                    // the return value to at the end of the array after the last argument (index argsCnt)
    	                    // unless the return value should be undefined
    	                    var argsCnt = args.length;
    	                    // get a temporary hold of the args (released after we notify SpawnJSRuntime)
    	                    var argsId = SpawnJSInterop.spawnJSObjectHold(args);
    	                    // notify SpawnJSRuntime with the argsId and the cnt
    	                    handleCallback(callbackId, argsId, argsCnt);
    	                    // release the args
    	                    SpawnJSInterop.spawnJSObjectRelease(argsId);
    	                    // if it was a 1 time use callback, release it
    	                    if (once) delete SpawnJSInterop._callbacks[callbackIdPair];
    	                    // return what is in index argsCnt (the designated place .Net will write to if there is a return value)
    	                    return args[argsCnt];
    	                };
    	                SpawnJSInterop._callbacks[callbackIdPair] = value;
    	            }
    	            parent[propertyName] = value;
    	        }
    	        // set property to using a SpawnJSInterop[methodName](value) call
    	        // methodName can be a SpawnJSInterop methodName or a methodIndex
    	        // revivers are Javascript methods
    	        // JSImport
    	        // void _propertySetWithReviver(double sjsId, string key, string value, double reviverIndex);
    	        // void _propertySetWithReviver(double sjsId, string key, double value, double reviverIndex);
    	        // void _propertySetWithReviver(double sjsId, string key, string value, double reviverIndex, string reviverConfig);
    	        // void _propertySetWithReviver(double sjsId, string key, double value, double reviverIndex, string reviverConfig);
    	        // void _propertySetWithReviver(double sjsId, string key, string value, double reviverIndex, double reviverConfig);
    	        // void _propertySetWithReviver(double sjsId, string key, double value, double reviverIndex, double reviverConfig);
    	        // void _propertySetWithReviver(double sjsId, string key, string value, double reviverIndex, bool reviverConfig);
    	        // void _propertySetWithReviver(double sjsId, string key, double value, double reviverIndex, bool reviverConfig);
    	        static propertySetWithReviver(sjsId, key, value, methodName, reviverConfig) {
    	            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
    	            if (obj === void 0 || obj === null) throw new Error('obj null or undefined');
    	            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
    	            if (shortCircuit) return;
    	            var reviver = typeof methodName === 'string' ? SpawnJSInterop[methodName] : SpawnJSInterop._methodMap[methodName];
    	            // if SpawnJSInterop does not have the reviver, try the glboalThis. This allows global types to be used
    	            if (!reviver) reviver = globalThis[methodName];
    	            if (!reviver) throw new Error(`Reviver not found: ${methodName}`);
    	            value = reviver(propertyName, value, true, reviverConfig);
    	            parent[propertyName] = value;
    	        }

    	        // Interop Calls
    	        // Interop calls are calls made indirectly via _spawnJSInteropCall and _spawnJSInteropCallAsync.
    	        // Because they go through those methods they can return and recieve any data type the marshallers support

    	        // call a property constructor
    	        // InteropCall
    	        // SpawnJSObjectReference
    	        // <double, string, object?[]?, SpawnJSObjectReference>
    	        static propertyNewApply(sjsId, key, args) {
    	            var ret = undefined;
    	            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
    	            var pathInfo = SpawnJSInterop.pathObjectInfo(obj, key);
    	            if (pathInfo.shortCircuit) return ret;
    	            var ret = !args ? new pathInfo.target() : new pathInfo.target(...args);
    	            return ret;
    	        }
    	        // call a property constructor
    	        // InteropCall
    	        // SpawnJSObjectReference
    	        // <double, string, ..., T>
    	        static propertyNew(sjsId, key, ...args) {
    	            var ret = undefined;
    	            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
    	            var pathInfo = SpawnJSInterop.pathObjectInfo(obj, key);
    	            if (pathInfo.shortCircuit) return ret;
    	            var ret = !args ? new pathInfo.target() : new pathInfo.target(...args);
    	            return ret;
    	        }
    	        // call a property
    	        // InteropCall
    	        // SpawnJSObjectReference
    	        // <double, string, object?[]?, T>
    	        static propertyCallApply(sjsId, key, args) {
    	            var ret = undefined;
    	            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
    	            var pathInfo = SpawnJSInterop.pathObjectInfo(obj, key);
    	            if (pathInfo.shortCircuit) return ret;
    	            var ret = pathInfo.target.apply(pathInfo.parent, args);
    	            if (typeof ret === 'function') {
    	                ret = ret.bind(pathInfo.parent);
    	            }
    	            return ret;
    	        }
    	        // call a property
    	        // InteropCall
    	        // SpawnJSObjectReference
    	        // <double, string, ..., T>
    	        static propertyCall(sjsId, key, ...args) {
    	            var ret = undefined;
    	            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
    	            var pathInfo = SpawnJSInterop.pathObjectInfo(obj, key);
    	            if (pathInfo.shortCircuit) return ret;
    	            var ret = pathInfo.target.apply(pathInfo.parent, args);
    	            if (typeof ret === 'function') {
    	                ret = ret.bind(pathInfo.parent);
    	            }
    	            return ret;
    	        }
    	        // useful when .Net Wasm wants to clonme a JSObjectReference or simply convert from one type to another
    	        // InteropCall
    	        // SpawnJSObjectReference
    	        // <TIn, TResult>
    	        static returnMe(value) {
    	            return value;
    	        }
    	        // InteropCall
    	        // ByteArrayMarshaller
    	        // <double, SpawnJSObjectReference, double, double, double, VoidType>
    	        static writeArrayBufferViewToHeap(dotnetId, arrayBufferView, srcOffset, destOffset, byteLength) {
    	            if (!arrayBufferView) throw new Error('writeArrayBufferViewToHeap arrayBufferView is required');
    	            if (byteLength === 0) return 0;
    	            var srcLength = arrayBufferView.byteLength;
    	            if (byteLength == -1) byteLength = srcLength;
    	            if (byteLength < -1) throw new Error('Invalid byteLength');
    	            // get the .Net heap
    	            var instance = SpawnJSInterop.getInstace(dotnetId);
    	            var buffer = instance.getHeap();
    	            var bufferView = new Uint8Array(buffer, destOffset, byteLength);
    	            // get a view of the exact source we want
    	            var offset = arrayBufferView.byteOffset + srcOffset;
    	            var sourceView = new Uint8Array(arrayBufferView.buffer, offset, byteLength);
    	            // copy to the .Net heap
    	            bufferView.set(sourceView);
    	            // return the bytes copied
    	            return byteLength;
    	        }
    	        // called using Call<>
    	        // InteropCall
    	        // TaskMarshaller
    	        // <double, int, SpawnJSObjectReference>
    	        static propertySetNewPromise(sjsId, key) {
    	            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
    	            if (obj === void 0 || obj === null) throw new Error('obj null or undefined');
    	            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
    	            if (shortCircuit) return;
    	            var promise = SpawnJSInterop.newEasyPromise();
    	            parent[propertyName] = promise;
    	            return promise;
    	        }
    	        // called using Call<>
    	        // InteropCall
    	        // TaskMarshaller
    	        // <double, string, VoidType>
    	        static propertySetResolvedPromise(sjsId, key, value) {
    	            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
    	            if (obj === void 0 || obj === null) throw new Error('obj null or undefined');
    	            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
    	            if (shortCircuit) return;
    	            parent[propertyName] = Promise.resolve(value);
    	        }
    	        // called using Call<>
    	        // InteropCall
    	        // TaskMarshaller
    	        // <double, string, string, VoidType>
    	        static propertySetRejectedPromise(sjsId, key, value) {
    	            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
    	            if (obj === void 0 || obj === null) throw new Error('obj null or undefined');
    	            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
    	            if (shortCircuit) return;
    	            parent[propertyName] = Promise.reject(value);
    	        }
    	        // InteropCall
    	        // SpawnJSRuntime
    	        // <double, long>
    	        static getHeapSize(dotnetId) {
    	            var instance = SpawnJSInterop.getInstace(dotnetId);
    	            var buffer = instance.getHeap();
    	            return buffer.byteLength;
    	        }
    	        // full ? strict equality : loose equality
    	        // InteropCall
    	        // SpawnJSRuntime
    	        // <T1, T2, bool, bool>
    	        static objectEquals(obj1, obj2, full) {
    	            return full ? obj1 === obj2 : obj1 == obj2;
    	        }

    	        static detachedEventCheck() {
    	            for (const dotnetIdExisting in SpawnJSInterop._instances) {
    	                if (Object.hasOwn(SpawnJSInterop._instances, dotnetIdExisting)) {
    	                    var instanceInfo = SpawnJSInterop._instances[dotnetIdExisting];
    	                    instanceInfo.getHeap();
    	                }
    	            }
    	        }
    	        static getInstace(dotnetId) {
    	            return SpawnJSInterop._instances[dotnetId];
    	        }
    	        static _getInstaceFromDotNet(dotnet) {
    	            for (const dotnetIdExisting in SpawnJSInterop._instances) {
    	                if (Object.hasOwn(SpawnJSInterop._instances, dotnetIdExisting)) {
    	                    var existingInfo = SpawnJSInterop._instances[dotnetIdExisting];
    	                    if (existingInfo.dotnet == dotnet) {
    	                        return existingInfo;
    	                    }
    	                }
    	            }
    	        }
    	        static __replacerJson(key, value, directCall) {
    	            if (directCall) value = JSON.stringify(value);
    	            return value;
    	        }
    	        static __reviverJson(key, value, directCall) {
    	            if (directCall) value = JSON.parse(value);
    	            return value;
    	        }
    	        static _unregisterInstance(dotnetId) {
    	            delete SpawnJSInterop._instances[dotnetId];
    	        }
    	        static _getMappedMethodNames() {
    	            return SpawnJSInterop._methodMapNames;
    	        }
    	        // array of { name, reviver }
    	        static registerReplacers(replacers) {
    	            var cnt = 0;
    	            if (!replacers) return cnt;
    	            for (var replacerObj of replacers) {
    	                var succ = SpawnJSInterop.registerReplacer(replacerObj.name, replacerObj.replacer);
    	                if (succ) cnt++;
    	            }
    	            return cnt;
    	        }
    	        static registerReplacer(name, replacer) {
    	            if (name.indexOf('__replacer') !== 0) throw new Error('Replacer names must start with __replacer');
    	            if (SpawnJSInterop[name]) {
    	                // already exists. fail quitely as it could just mean another app loaded that uses the same replacers
    	                return false;
    	            }
    	            SpawnJSInterop[name] = replacer;
    	            SpawnJSInterop.refreshMethodMap();
    	            return true;
    	        }
    	        // array of { name, reviver }
    	        static registerRevivers(revivers) {
    	            var cnt = 0;
    	            if (!revivers) return cnt;
    	            for (var reviverObj of revivers) {
    	                var succ = SpawnJSInterop.registerReviver(reviverObj.name, reviverObj.reviver);
    	                if (succ) cnt++;
    	            }
    	            return cnt;
    	        }
    	        static registerReviver(name, reviver) {
    	            if (name.indexOf('__reviver') !== 0) throw new Error('Reviver names must start with __reviver');
    	            if (SpawnJSInterop[name]) {
    	                // already exists. fail quitely as it could just mean another app loaded that uses the same revivers
    	                return false;
    	            }
    	            SpawnJSInterop[name] = reviver;
    	            SpawnJSInterop.refreshMethodMap();
    	            return true;
    	        }
    	        // puts objectToHold (value of ANY type) into spawnJSObjects and returns the id
    	        // the object will stay held until released and it must be released to prevent memory leaks
    	        static spawnJSObjectHold(objectToHold) {
    	            if (objectToHold === globalThis) return -1;
    	            if (objectToHold === undefined) return -2;
    	            if (objectToHold === null) return -3;
    	            if (objectToHold === SpawnJSInterop.spawnJSObjects) return -4;
    	            if (objectToHold === SpawnJSInterop) return -5;
    	            var sjsId = ++SpawnJSInterop._sjsObjectIdNext;
    	            SpawnJSInterop.spawnJSObjects[sjsId] = objectToHold;
    	            return sjsId;
    	        }
    	        // get an object from the hold
    	        static spawnJSObjectGet(sjsId) {
    	            switch (sjsId) {
    	                case -1: return globalThis;
    	                case -2: return undefined;
    	                case -3: return null;
    	                case -4: return SpawnJSInterop.spawnJSObjects;
    	                case -5: return SpawnJSInterop;
    	            }
    	            if (!SpawnJSInterop.spawnJSObjectHoldExists(sjsId)) {
    	                throw new Error('SpawnJSObjectGet object not found.');
    	            }
    	            var ret = SpawnJSInterop.spawnJSObjects[sjsId];
    	            // passive revive (allows things like refreshing heap views)
    	            ret = SpawnJSInterop.reviveValue(null, ret, false);
    	            return ret;
    	        }// get an obejct from the hold and replace it with a new one
    	        static spawnJSObjectGetAndReplace(sjsId, newValue) {
    	            switch (sjsId) {
    	                case -1: return globalThis;
    	                case -2: return undefined;
    	                case -3: return null;
    	                case -4: return SpawnJSInterop.spawnJSObjects;
    	                case -5: return SpawnJSInterop;
    	            }
    	            if (!SpawnJSInterop.spawnJSObjectHoldExists(sjsId)) {
    	                throw new Error('SpawnJSObjectGet object not found.');
    	            }
    	            var ret = SpawnJSInterop.spawnJSObjects[sjsId];
    	            SpawnJSInterop.spawnJSObjects[sjsId] = newValue;
    	            return ret;
    	        }
    	        static heapViewRefresh(heapViewInfo) {
    	            var needsCreate = !heapViewInfo.buffer;
    	            var needsRefresh = !heapViewInfo.buffer || heapViewInfo.buffer.detached;
    	            if (needsRefresh) {
    	                var instance = SpawnJSInterop.getInstace(heapViewInfo.dotnetId);
    	                heapViewInfo.buffer = instance.getHeap();
    	                var value = null;
    	                var length = heapViewInfo.length === -1 ? /* entire buffer */ heapViewInfo.buffer.byteLength - heapViewInfo.offset : heapViewInfo.length;
    	                if (heapViewInfo.viewType === 13) {
    	                    // ArrayBuffer requested
    	                    if (heapViewInfo.copy) {
    	                        // create a copy
    	                        value = heapViewInfo.buffer.slice(heapViewInfo.offset, heapViewInfo.offset + length);
    	                    } else {
    	                        // can't use offet and length when not copying the heap asn ArrayBuffer view
    	                        if (heapViewInfo.offset != 0) throw new Error('Offset and length not supported creating an ArrayBuffer heap view without a copy');
    	                        value = heapViewInfo.buffer;
    	                    }
    	                } else if (heapViewInfo.viewType === 14) {
    	                    // SharedArrayBuffer requested
    	                    if (heapViewInfo.copy) {
    	                        // create a copy
    	                        var uint8ArrayHeap = new Uint8Array(heapViewInfo.buffer, heapViewInfo.offset, length);
    	                        value = new globalThis.SharedArrayBuffer(length);
    	                        value.set(uint8ArrayHeap);
    	                    } else {
    	                        // can't get a live SharedArrayBuffer view of the heap
    	                        throw new Error('Cannot get a live SharedArrayBuffer view of the heap');
    	                    }
    	                } else {
    	                    // ArrayBufferView (TypedArray or DataView)
    	                    var liveView = new heapViewInfo.ctor(heapViewInfo.buffer, heapViewInfo.offset, length);
    	                    value = heapViewInfo.copy ? liveView.slice() : liveView;
    	                }
    	                // copies do not get (or need) their view refreshed as it will not detach
    	                if (!heapViewInfo.copy) value._heapViewInfo = heapViewInfo;
    	                heapViewInfo.bufferLength = heapViewInfo.buffer.byteLength;
    	                heapViewInfo.sizeHistory.push(heapViewInfo.bufferLength);
    	                heapViewInfo.value = value;
    	                if (SpawnJSInterop.verbose) {
    	                    if (!needsCreate) console.log('Heapview refreshed:', heapViewInfo);
    	                    else console.log('Heapview created:', heapViewInfo);
    	                }
    	            }
    	            return heapViewInfo.value;
    	        }
    	        static getArrayBufferViewConstructor(viewType) {
    	            var ctor = SpawnJSInterop.HeapViewCtors[viewType];
    	            if (viewType === 13 || viewType === 14) return null;   // ArrayBuffer
    	            if (!ctor) throw new Error(`Unsupported or missing ArrayBufferView constructor for enum index: ${viewType}`);
    	            return ctor;
    	        }
    	        static __replacerHeapView(key, value, directCall, reviverConfig) {
    	            if (directCall) {
    	                if (value && typeof value === 'object' && SpawnJSInterop._in('_heapViewInfo', value)) ;
    	            }
    	            return value;
    	        }
    	        static __reviverHeapView(key, value, directCall, reviverConfig) {
    	            if (!directCall && value && typeof value === 'object' && SpawnJSInterop._in('_heapViewInfo', value)) {
    	                // auto-reattach if needed
    	                // this allows creating a fresh HeapView if needed on `set` and `call` (with the exception of call reattach only working if the view is in teh root args list. no object walking is done)
    	                var heapViewInfo = value._heapViewInfo;
    	                value = SpawnJSInterop.heapViewRefresh(heapViewInfo);
    	            }
    	            return value;
    	        }
    	        // create a new Promsie with the resolve and reject methods attached to the promise for easy calling from .Net
    	        static newEasyPromise() {
    	            var _resolve = null;
    	            var _reject = null;
    	            var promise = new Promise((resolve, reject) => {
    	                _resolve = resolve;
    	                _reject = reject;
    	            });
    	            promise.resolve = _resolve;
    	            promise.reject = _reject;
    	            return promise
    	        }
    	        // Reviver used by BigIntegerMarshaller: a BigInteger crosses as its decimal string, because a JS
    	        // number cannot hold one exactly, and is revived into a real BigInt here.
    	        // Revivers are called as (key, value, directCall, config) by propertySetWithReviver - a plain
    	        // (value) signature would silently revive the PROPERTY NAME instead of the value.
    	        static stringToBigInt(key, value) {
    	            if (!globalThis.BigInt) throw new Error('BigInt not supported on this platform');
    	            return value === undefined || value === null ? null : globalThis.BigInt(value);
    	        }
    	        // object constructor names
    	        // returns string[]
    	        static getConstructorNames(obj) {
    	            var constructorNames = [];
    	            if (obj === void 0 || obj === null) return constructorNames;
    	            var o = obj;
    	            var cName;
    	            while (1) {
    	                o = Object.getPrototypeOf(o);
    	                cName = o?.constructor?.name;
    	                if (!cName) break;
    	                if (constructorNames.indexOf(cName) !== -1) continue;
    	                constructorNames.push(cName);
    	            }
    	            return constructorNames;
    	        }
    	        // returns string[] of the target's property names.
    	        // hasOwnProperty true restricts to the object's own enumerable keys (Object.keys); false walks the
    	        // prototype chain too, which is what you need to enumerate a DOM object's API rather than just the
    	        // handful of own properties it happens to carry.
    	        static objectKeys(target, hasOwnProperty) {
    	            if (target === void 0 || target === null) return [];
    	            if (hasOwnProperty) return Object.keys(target);
    	            var keys = [];
    	            for (var key in target) {
    	                if (keys.indexOf(key) === -1) keys.push(key);
    	            }
    	            return keys;
    	        }
    	        static reviveValue(key, initialValue, directCall) {
    	            // Pipe the value through each reviver sequentially
    	            return SpawnJSInterop._revivers.reduce((currentValue, currentReviver) => {
    	                // Short-circuit: if a previous reviver dropped the value, skip the rest
    	                if (currentValue === undefined) return undefined;
    	                return currentReviver(key, currentValue, directCall); // the true tells the reviver this is a call revive as opposed to a propertySet revive
    	            }, initialValue);
    	        }
    	        static replaceValue(key, initialValue, directCall) {
    	            // Pipe the value through each reviver sequentially
    	            return SpawnJSInterop._replacers.reduce((currentValue, currentReplacer) => {
    	                // Short-circuit: if a previous reviver dropped the value, skip the rest
    	                if (currentValue === undefined) return undefined;
    	                return currentReplacer(key, currentValue, directCall); // the true tells the reviver this is a call revive as opposed to a propertySet revive
    	            }, initialValue);
    	        }
    	        // returns the types the object inherits from
    	        // returns string[]
    	        static getPropertyConstructorNames(parent, key) {
    	            return SpawnJSInterop.getConstructorNames(parent[key]);
    	        }
    	        // converts to an error string or returns a generic error string
    	        static errorToString(error) {
    	            if (!error) return "Unknown error";
    	            // Handle native Error objects (e.g., new Error(), TypeError)
    	            if (error instanceof Error) {
    	                error = error.message;
    	            }
    	            else if (typeof error === 'object') {
    	                try {
    	                    error = error.message || JSON.stringify(error);
    	                } catch {
    	                    error = String(error);
    	                }
    	            }
    	            error ??= String(error);
    	            error ??= "Unknown error";
    	            return error;
    	        }
    	        // Main .Net to JS entrypoint
    	        static async _spawnJSInteropLoadExportsAsync(dotnetId, assemblyName) {
    	            var dotnet = SpawnJSInterop.spawnJSObjectGet(dotnetId);
    	            var assemblyExports = await dotnet.getAssemblyExports(assemblyName);
    	            var spawnJSExports = assemblyName.split('.').reduce((acc, key) => acc[key], assemblyExports);
    	            dotnet.spawnJSExports = spawnJSExports;
    	        }
    	        // prepares the variable for .Net based on returnType
    	        static _serializeToNet(returnType, ret) {
    	            switch (returnType) {
    	                case 0:  // void
    	                    return;
    	                case 6:  // SpawnJSObject - Number
    	                case 7:  // SpawnJSObjectNonNullable - Number
    	                    return SpawnJSInterop.spawnJSObjectHold(ret);
    	                case 8:  // Json
    	                    return JSON.stringify(ret);
    	                case 1:  // Double
    	                case 2:  // Boolean
    	                case 3:  // DoubleNullable
    	                case 4:  // BooleanNullable
    	                case 9:  // Int32
    	                case 10: // Int32Nullable
    	                    return ret;
    	                case 5:  // String
    	                    if (ret && typeof ret !== 'string') {
    	                        ret = Object(ret).toString();
    	                    }
    	                    return ret;
    	            }
    	            // the default is to return as is
    	            return ret;
    	        }
    	        static wasmMemoryBuffer(dotnet) {
    	            var found = SpawnJSInterop.#findWasmMemory(dotnet);
    	            if (!found) throw new Error('SpawnJSInterop: could not reach the WebAssembly memory buffer');
    	            return found.buffer;
    	        }
    	        // returns the name of the path the memory buffer was found under, or '' if it was not found
    	        static wasmMemoryBufferSource(dotnet) {
    	            var found = SpawnJSInterop.#findWasmMemory(dotnet);
    	            return found ? found.source : '';
    	        }
    	        static #findWasmMemory(dotnet) {
    	            var rt = dotnet;
    	            if (!rt) return null;
    	            var candidates = [
    	                ['Module.HEAPU8.buffer', () => rt.Module?.HEAPU8?.buffer],
    	                ['Module.wasmMemory.buffer', () => rt.Module?.wasmMemory?.buffer],
    	                ['localHeapViewU8().buffer', () => rt.localHeapViewU8?.()?.buffer],
    	                ['getHeapU8().buffer', () => rt.getHeapU8?.()?.buffer],
    	            ];
    	            for (var i = 0; i < candidates.length; i++) {
    	                var buffer;
    	                try { buffer = candidates[i][1](); } catch (ex) { continue; }
    	                if (buffer && typeof buffer.byteLength === 'number' && buffer.byteLength > 0) {
    	                    return { buffer: buffer, source: candidates[i][0] };
    	                }
    	            }
    	            return null;
    	        }
    	        // safely checks for a property existence
    	        // safely means will not throw, which is needed as simply checking for a
    	        // a proeprty can throw an exception (notably on cross-origin windows)
    	        static _in(key, obj) {
    	            if (obj === null || obj === void 0) return false;
    	            try {
    	                return key in Object(obj);
    	            } catch { }
    	            return false;
    	        }
    	        // returns the path info based on a base object and a property path: `window?.location.href`
    	        static pathObjectInfo(rootObject, path) {
    	            if (rootObject === null || rootObject === void 0) {
    	                // callers must call with the globalThis if they wish to use it as the rootObject.
    	                throw new Error('spawnJSInterop.pathObjectInfo error: rootObject cannot be null');
    	            }
    	            var parent = rootObject;
    	            var target;
    	            var propertyName;
    	            var shortCircuit = false;
    	            if (typeof path === 'string' && !(SpawnJSInterop._in(path, parent))) {
    	                var parts = path.split('.');
    	                propertyName = parts[parts.length - 1];
    	                var part;
    	                for (var i = 0; i < parts.length - 1; i++) {
    	                    part = parts[i];
    	                    if (part[part.length - 1] === '?') {
    	                        // ? null conditonal found
    	                        // if parent does not exist allow undefined/null parent instead of throwing exception
    	                        part = part.substring(0, part.length - 1);
    	                        parent = parent[part];
    	                        if (parent === void 0 || parent === null) {
    	                            shortCircuit = true;
    	                            break;
    	                        }
    	                    }
    	                    else {
    	                        parent = parent[part];
    	                    }
    	                }
    	                if (!shortCircuit) {
    	                    target = parent[propertyName];
    	                }
    	            }
    	            else {
    	                propertyName = path;
    	                target = parent[propertyName];
    	            }
    	            return {
    	                shortCircuit,   // bool - true if the pathfinding short circuited due to a null-conditional
    	                parent,         // any - only null or undefined if short circuited due to a null-conditional
    	                propertyName,   // any
    	                target,         // any
    	            };
    	        }

    	        // The URL this app was LOADED from - the origin of its own main.* / _framework, NOT the host
    	        // page's document.baseURI. Under a CDN load the page and the app live at different URLs, and every
    	        // worker entry (main.classic.js / main.module.js / _framework/*) must resolve against the APP's
    	        // origin. document.baseURI is a page-coupled Blazor-ism that hands back the page root instead.
    	        //
    	        // Derived per-runtime from THIS app's OWN dotnetRuntime, so two SpawnJS apps loaded from different
    	        // origins on one page each get their own base - a module-scope import.meta.url could not, because
    	        // the class-definition guard means app B's lib.module.js body never re-runs.
    	        //
    	        // Fail-loud multi-candidate, the same shape as #findWasmMemory: the runtime exposes its origin
    	        // under different shapes across scopes/versions, so every known shape is tried and the one that
    	        // worked is reportable via appBaseUriSource(). Returns '' if none resolve, so the caller can fall
    	        // back rather than silently build worker URLs against a wrong base.
    	        static appBaseUri(dotnet) {
    	            var found = this.#findAppBaseUri(dotnet);
    	            return found ? found.uri : '';
    	        }
    	        // Which candidate produced appBaseUri(), or '' - diagnostic, mirrors wasmMemoryBufferSource().
    	        static appBaseUriSource(dotnet) {
    	            var found = this.#findAppBaseUri(dotnet);
    	            return found ? found.source : '';
    	        }
    	        static import(src) {
    	            return import(src);
    	        }
    	        // The app-root normalizer, exposed so it is diagnosable and testable directly. The test suite
    	        // drives THIS function rather than a copy of the logic, so the production path is what is covered.
    	        static appRootFromLoadUrl(raw) {
    	            return this.#appRootFromLoadUrl(raw);
    	        }
    	        // Normalizes a URL some runtime artifact was loaded from into the app root, with a trailing slash.
    	        //
    	        // Every boot artifact - the runtime entry (dotnet.js / dotnet.<fingerprint>.js) and every resource
    	        // in the boot manifest (.wasm/.dll assemblies, ICU .dat/.blat, .pdb symbols) - lives in the app's
    	        // framework folder, so the app root is that folder's PARENT. Anything else was loaded from the app
    	        // root itself: a bundled entrypoint (main.classic.js / main.module.js) sits beside index.html, and
    	        // that is what SpawnDev.SpawnJS.WebWorkers bundles before 2.1.9 reported here.
    	        //
    	        // The framework folder is identified by WHAT was loaded, never by what the folder is NAMED. It is
    	        // "_framework" in a normal publish, but a published app may rename it - WebWorkers'
    	        // SpawnJSWebWorkersFrameworkFolderName does exactly that, because a browser extension may not have
    	        // a root folder starting with '_'. Matching the literal name returned the framework folder itself
    	        // as the app root there, and every URL built on it (worker entrypoints above all) resolved one
    	        // level too deep.
    	        static #appRootFromLoadUrl(raw) {
    	            if (typeof raw !== 'string' || raw.length === 0) return '';
    	            if (raw.startsWith('blob:')) return '';
    	            var url;
    	            try { url = new URL(raw, self?.location?.href); } catch (ex) { return ''; }
    	            var segments = url.pathname.split('/');   // leading '' from the root slash
    	            var file = segments.pop();                // '' when the url already names a directory
    	            // A boot artifact is one level below the app root, whatever its folder is called. Guarded on
    	            // length so an artifact served from the origin root cannot walk above it.
    	            if (file && this.#isBootArtifact(file) && segments.length > 1) segments.pop();
    	            return url.origin + segments.join('/') + '/';
    	        }
    	        // True for the runtime entry and for boot-manifest resources - the files that only ever live in
    	        // the app's framework folder. Deliberately does NOT match a bundled entrypoint (main.*.js), which
    	        // sits at the app root.
    	        static #isBootArtifact(fileName) {
    	            return /^dotnet(\..+)?\.m?js$/i.test(fileName)
    	                || /\.(wasm|dll|dat|blat|pdb)$/i.test(fileName);
    	        }
    	        static #findAppBaseUri(dotnet) {
    	            if (!dotnet) return null;
    	            var candidates = [
    	                // PROVEN primary (measured across scopes): dotnet.js's own module URL, i.e.
    	                // appRoot/_framework/dotnet.<fp>.js - itself import.meta-derived, so it is the real CDN
    	                // origin under a CDN load, not the host page. Can be a blob: URL in some worker configs,
    	                // which #appRootFromLoadUrl rejects so the resolver falls through to the next candidate.
    	                ['Module.mainScriptUrlOrBlob', () => dotnet.Module?.mainScriptUrlOrBlob],
    	                // Robust backup: every boot resource carries an absolute resolvedUrl (appRoot/_framework/*),
    	                // always populated even when mainScriptUrlOrBlob is a blob.
    	                ['getConfig().resources.assembly[0].resolvedUrl', () => dotnet.getConfig?.()?.resources?.assembly?.[0]?.resolvedUrl],
    	            ];
    	            for (var i = 0; i < candidates.length; i++) {
    	                var raw;
    	                try { raw = candidates[i][1](); } catch (ex) { continue; }
    	                var uri = this.#appRootFromLoadUrl(raw);
    	                if (uri) return { uri: uri, source: candidates[i][0] };
    	            }
    	            return null;
    	        }
    	    }
    	    globalThis.SpawnJSInterop = SpawnJSInterop;
    	})();
    	return SpawnDev_SpawnJS_9pjx9wsgr4_lib_module$1;
    }

    var SpawnDev_SpawnJS_9pjx9wsgr4_lib_moduleExports = requireSpawnDev_SpawnJS_9pjx9wsgr4_lib_module();
    var SpawnDev_SpawnJS_9pjx9wsgr4_lib_module = /*@__PURE__*/getDefaultExportFromCjs(SpawnDev_SpawnJS_9pjx9wsgr4_lib_moduleExports);

    var ___2F_SpawnDev_SpawnJS_9pjx9wsgr4_lib_module_js = /*#__PURE__*/_mergeNamespaces({
        __proto__: null,
        default: SpawnDev_SpawnJS_9pjx9wsgr4_lib_module
    }, [SpawnDev_SpawnJS_9pjx9wsgr4_lib_moduleExports]);

    //! Licensed to the .NET Foundation under one or more agreements.
    //! The .NET Foundation licenses this file to you under the MIT license.
    var e=false;const t=async()=>WebAssembly.validate(new Uint8Array([0,97,115,109,1,0,0,0,1,4,1,96,0,0,3,2,1,0,10,8,1,6,0,6,64,25,11,11])),o=async()=>WebAssembly.validate(new Uint8Array([0,97,115,109,1,0,0,0,1,5,1,96,0,1,123,3,2,1,0,10,15,1,13,0,65,1,253,15,65,2,253,15,253,128,2,11])),n=async()=>WebAssembly.validate(new Uint8Array([0,97,115,109,1,0,0,0,1,5,1,96,0,1,123,3,2,1,0,10,10,1,8,0,65,0,253,15,253,98,11])),r=Symbol.for("wasm promise_control");function i(e,t){let o=null;const n=new Promise((function(n,r){o={isDone:false,promise:null,resolve:t=>{o.isDone||(o.isDone=true,n(t),e&&e());},reject:e=>{o.isDone||(o.isDone=true,r(e),t&&t());}};}));o.promise=n;const i=n;return i[r]=o,{promise:i,promise_control:o}}function s(e){return e[r]}function a(e){e&&function(e){return void 0!==e[r]}(e)||Be(false,"Promise is not controllable");}const l="__mono_message__",c=["debug","log","trace","warn","info","error"],d="MONO_WASM: ";let u,f,m,g,p,h;function w(e){g=e;}function b(e){if(Pe.diagnosticTracing){const t="function"==typeof e?e():e;console.debug(d+t);}}function y(e,...t){console.info(d+e,...t);}function v(e,...t){console.info(e,...t);}function E(e,...t){console.warn(d+e,...t);}function _(e,...t){if(t&&t.length>0&&t[0]&&"object"==typeof t[0]){if(t[0].silent)return;if(t[0].toString)return void console.error(d+e,t[0].toString())}console.error(d+e,...t);}function x(e,t,o){return function(...n){try{let r=n[0];if(void 0===r)r="undefined";else if(null===r)r="null";else if("function"==typeof r)r=r.toString();else if("string"!=typeof r)try{r=JSON.stringify(r);}catch(e){r=r.toString();}t(o?JSON.stringify({method:e,payload:r,arguments:n.slice(1)}):[e+r,...n.slice(1)]);}catch(e){m.error(`proxyConsole failed: ${e}`);}}}function j(e,t,o){f=t,g=e,m={...t};const n=`${o}/console`.replace("https://","wss://").replace("http://","ws://");u=new WebSocket(n),u.addEventListener("error",A),u.addEventListener("close",S),function(){for(const e of c)f[e]=x(`console.${e}`,T,true);}();}function R(e){let t=30;const o=()=>{u?0==u.bufferedAmount||0==t?(e&&v(e),function(){for(const e of c)f[e]=x(`console.${e}`,m.log,false);}(),u.removeEventListener("error",A),u.removeEventListener("close",S),u.close(1e3,e),u=void 0):(t--,globalThis.setTimeout(o,100)):e&&m&&m.log(e);};o();}function T(e){u&&u.readyState===WebSocket.OPEN?u.send(e):m.log(e);}function A(e){m.error(`[${g}] proxy console websocket error: ${e}`,e);}function S(e){m.debug(`[${g}] proxy console websocket closed: ${e}`,e);}function D(){Pe.preferredIcuAsset=O(Pe.config);let e="invariant"==Pe.config.globalizationMode;if(!e)if(Pe.preferredIcuAsset)Pe.diagnosticTracing&&b("ICU data archive(s) available, disabling invariant mode");else {if("custom"===Pe.config.globalizationMode||"all"===Pe.config.globalizationMode||"sharded"===Pe.config.globalizationMode){const e="invariant globalization mode is inactive and no ICU data archives are available";throw _(`ERROR: ${e}`),new Error(e)}Pe.diagnosticTracing&&b("ICU data archive(s) not available, using invariant globalization mode"),e=true,Pe.preferredIcuAsset=null;}const t="DOTNET_SYSTEM_GLOBALIZATION_INVARIANT",o=Pe.config.environmentVariables;if(void 0===o[t]&&e&&(o[t]="1"),void 0===o.TZ)try{const e=Intl.DateTimeFormat().resolvedOptions().timeZone||null;e&&(o.TZ=e);}catch(e){y("failed to detect timezone, will fallback to UTC");}}function O(e){var t;if((null===(t=e.resources)||void 0===t?void 0:t.icu)&&"invariant"!=e.globalizationMode){const t=e.applicationCulture||(ke?globalThis.navigator&&globalThis.navigator.languages&&globalThis.navigator.languages[0]:Intl.DateTimeFormat().resolvedOptions().locale),o=e.resources.icu;let n=null;if("custom"===e.globalizationMode){if(o.length>=1)return o[0].name}else t&&"all"!==e.globalizationMode?"sharded"===e.globalizationMode&&(n=function(e){const t=e.split("-")[0];return "en"===t||["fr","fr-FR","it","it-IT","de","de-DE","es","es-ES"].includes(e)?"icudt_EFIGS.dat":["zh","ko","ja"].includes(t)?"icudt_CJK.dat":"icudt_no_CJK.dat"}(t)):n="icudt.dat";if(n)for(let e=0;e<o.length;e++){const t=o[e];if(t.virtualPath===n)return t.name}}return e.globalizationMode="invariant",null}(new Date).valueOf();const C=class{constructor(e){this.url=e;}toString(){return this.url}};async function k(e,t){try{const o="function"==typeof globalThis.fetch;if(Se){const n=e.startsWith("file://");if(!n&&o)return globalThis.fetch(e,t||{credentials:"same-origin"});p||(h=Ne.require("url"),p=Ne.require("fs")),n&&(e=h.fileURLToPath(e));const r=await p.promises.readFile(e);return {ok:!0,headers:{length:0,get:()=>null},url:e,arrayBuffer:()=>r,json:()=>JSON.parse(r),text:()=>{throw new Error("NotImplementedException")}}}if(o)return globalThis.fetch(e,t||{credentials:"same-origin"});if("function"==typeof read)return {ok:!0,url:e,headers:{length:0,get:()=>null},arrayBuffer:()=>new Uint8Array(read(e,"binary")),json:()=>JSON.parse(read(e,"utf8")),text:()=>read(e,"utf8")}}catch(t){return {ok:false,url:e,status:500,headers:{length:0,get:()=>null},statusText:"ERR28: "+t,arrayBuffer:()=>{throw t},json:()=>{throw t},text:()=>{throw t}}}throw new Error("No fetch implementation available")}function I(e){return "string"!=typeof e&&Be(false,"url must be a string"),!M(e)&&0!==e.indexOf("./")&&0!==e.indexOf("../")&&globalThis.URL&&globalThis.document&&globalThis.document.baseURI&&(e=new URL(e,globalThis.document.baseURI).toString()),e}const U=/^[a-zA-Z][a-zA-Z\d+\-.]*?:\/\//,P=/[a-zA-Z]:[\\/]/;function M(e){return Se||Ie?e.startsWith("/")||e.startsWith("\\")||-1!==e.indexOf("///")||P.test(e):U.test(e)}let L,N=0;const $=[],z=[],W=new Map,F={"js-module-threads":true,"js-module-runtime":true,"js-module-dotnet":true,"js-module-native":true,"js-module-diagnostics":true},B={...F,"js-module-library-initializer":true},V={...F,dotnetwasm:true,heap:true,manifest:true},q={...B,manifest:true},H={...B,dotnetwasm:true},J={dotnetwasm:true,symbols:true},Z={...B,dotnetwasm:true,symbols:true},Q={symbols:true};function G(e){return !("icu"==e.behavior&&e.name!=Pe.preferredIcuAsset)}function K(e,t,o){null!=t||(t=[]),Be(1==t.length,`Expect to have one ${o} asset in resources`);const n=t[0];return n.behavior=o,X(n),e.push(n),n}function X(e){V[e.behavior]&&W.set(e.behavior,e);}function Y(e){Be(V[e],`Unknown single asset behavior ${e}`);const t=W.get(e);if(t&&!t.resolvedUrl)if(t.resolvedUrl=Pe.locateFile(t.name),F[t.behavior]){const e=ge(t);e?("string"!=typeof e&&Be(false,"loadBootResource response for 'dotnetjs' type should be a URL string"),t.resolvedUrl=e):t.resolvedUrl=ce(t.resolvedUrl,t.behavior);}else if("dotnetwasm"!==t.behavior)throw new Error(`Unknown single asset behavior ${e}`);return t}function ee(e){const t=Y(e);return Be(t,`Single asset for ${e} not found`),t}let te=false;async function oe(){if(!te){te=true,Pe.diagnosticTracing&&b("mono_download_assets");try{const e=[],t=[],o=(e,t)=>{!Z[e.behavior]&&G(e)&&Pe.expected_instantiated_assets_count++,!H[e.behavior]&&G(e)&&(Pe.expected_downloaded_assets_count++,t.push(se(e)));};for(const t of $)o(t,e);for(const e of z)o(e,t);Pe.allDownloadsQueued.promise_control.resolve(),Promise.all([...e,...t]).then((()=>{Pe.allDownloadsFinished.promise_control.resolve();})).catch((e=>{throw Pe.err("Error in mono_download_assets: "+e),Xe(1,e),e})),await Pe.runtimeModuleLoaded.promise;const n=async e=>{const t=await e;if(t.buffer){if(!Z[t.behavior]){t.buffer&&"object"==typeof t.buffer||Be(!1,"asset buffer must be array-like or buffer-like or promise of these"),"string"!=typeof t.resolvedUrl&&Be(!1,"resolvedUrl must be string");const e=t.resolvedUrl,o=await t.buffer,n=new Uint8Array(o);pe(t),await Ue.beforeOnRuntimeInitialized.promise,Ue.instantiate_asset(t,e,n);}}else J[t.behavior]?("symbols"===t.behavior&&(await Ue.instantiate_symbols_asset(t),pe(t)),J[t.behavior]&&++Pe.actual_downloaded_assets_count):(t.isOptional||Be(!1,"Expected asset to have the downloaded buffer"),!H[t.behavior]&&G(t)&&Pe.expected_downloaded_assets_count--,!Z[t.behavior]&&G(t)&&Pe.expected_instantiated_assets_count--);},r=[],i=[];for(const t of e)r.push(n(t));for(const e of t)i.push(n(e));Promise.all(r).then((()=>{Ce||Ue.coreAssetsInMemory.promise_control.resolve();})).catch((e=>{throw Pe.err("Error in mono_download_assets: "+e),Xe(1,e),e})),Promise.all(i).then((async()=>{Ce||(await Ue.coreAssetsInMemory.promise,Ue.allAssetsInMemory.promise_control.resolve());})).catch((e=>{throw Pe.err("Error in mono_download_assets: "+e),Xe(1,e),e}));}catch(e){throw Pe.err("Error in mono_download_assets: "+e),e}}}let ne=false;function re(){if(ne)return;ne=true;const e=Pe.config,t=[];if(e.assets)for(const t of e.assets)"object"!=typeof t&&Be(false,`asset must be object, it was ${typeof t} : ${t}`),"string"!=typeof t.behavior&&Be(false,"asset behavior must be known string"),"string"!=typeof t.name&&Be(false,"asset name must be string"),t.resolvedUrl&&"string"!=typeof t.resolvedUrl&&Be(false,"asset resolvedUrl could be string"),t.hash&&"string"!=typeof t.hash&&Be(false,"asset resolvedUrl could be string"),t.pendingDownload&&"object"!=typeof t.pendingDownload&&Be(false,"asset pendingDownload could be object"),t.isCore?$.push(t):z.push(t),X(t);else if(e.resources){const o=e.resources;o.wasmNative||Be(false,"resources.wasmNative must be defined"),o.jsModuleNative||Be(false,"resources.jsModuleNative must be defined"),o.jsModuleRuntime||Be(false,"resources.jsModuleRuntime must be defined"),K(z,o.wasmNative,"dotnetwasm"),K(t,o.jsModuleNative,"js-module-native"),K(t,o.jsModuleRuntime,"js-module-runtime"),o.jsModuleDiagnostics&&K(t,o.jsModuleDiagnostics,"js-module-diagnostics");const n=(e,t,o)=>{const n=e;n.behavior=t,o?(n.isCore=true,$.push(n)):z.push(n);};if(o.coreAssembly)for(let e=0;e<o.coreAssembly.length;e++)n(o.coreAssembly[e],"assembly",true);if(o.assembly)for(let e=0;e<o.assembly.length;e++)n(o.assembly[e],"assembly",!o.coreAssembly);if(0!=e.debugLevel&&Pe.isDebuggingSupported()){if(o.corePdb)for(let e=0;e<o.corePdb.length;e++)n(o.corePdb[e],"pdb",true);if(o.pdb)for(let e=0;e<o.pdb.length;e++)n(o.pdb[e],"pdb",!o.corePdb);}if(e.loadAllSatelliteResources&&o.satelliteResources)for(const e in o.satelliteResources)for(let t=0;t<o.satelliteResources[e].length;t++){const r=o.satelliteResources[e][t];r.culture=e,n(r,"resource",!o.coreAssembly);}if(o.coreVfs)for(let e=0;e<o.coreVfs.length;e++)n(o.coreVfs[e],"vfs",true);if(o.vfs)for(let e=0;e<o.vfs.length;e++)n(o.vfs[e],"vfs",!o.coreVfs);const r=O(e);if(r&&o.icu)for(let e=0;e<o.icu.length;e++){const t=o.icu[e];t.name===r&&n(t,"icu",false);}if(o.wasmSymbols)for(let e=0;e<o.wasmSymbols.length;e++)n(o.wasmSymbols[e],"symbols",false);}if(e.appsettings)for(let t=0;t<e.appsettings.length;t++){const o=e.appsettings[t],n=he(o);"appsettings.json"!==n&&n!==`appsettings.${e.applicationEnvironment}.json`||z.push({name:o,behavior:"vfs",cache:"no-cache",useCredentials:true});}e.assets=[...$,...z,...t];}async function ie(e){const t=await se(e);return await t.pendingDownloadInternal.response,t.buffer}async function se(e){try{return await ae(e)}catch(t){if(!Pe.enableDownloadRetry)throw t;if(Ie||Se)throw t;if(e.pendingDownload&&e.pendingDownloadInternal==e.pendingDownload)throw t;if(e.resolvedUrl&&-1!=e.resolvedUrl.indexOf("file://"))throw t;if(t&&404==t.status)throw t;e.pendingDownloadInternal=void 0,await Pe.allDownloadsQueued.promise;try{return Pe.diagnosticTracing&&b(`Retrying download '${e.name}'`),await ae(e)}catch(t){return e.pendingDownloadInternal=void 0,await new Promise((e=>globalThis.setTimeout(e,100))),Pe.diagnosticTracing&&b(`Retrying download (2) '${e.name}' after delay`),await ae(e)}}}async function ae(e){for(;L;)await L.promise;try{++N,N==Pe.maxParallelDownloads&&(Pe.diagnosticTracing&&b("Throttling further parallel downloads"),L=i());const t=await async function(e){if(e.pendingDownload&&(e.pendingDownloadInternal=e.pendingDownload),e.pendingDownloadInternal&&e.pendingDownloadInternal.response)return e.pendingDownloadInternal.response;if(e.buffer){const t=await e.buffer;return e.resolvedUrl||(e.resolvedUrl="undefined://"+e.name),e.pendingDownloadInternal={url:e.resolvedUrl,name:e.name,response:Promise.resolve({ok:!0,arrayBuffer:()=>t,json:()=>JSON.parse(new TextDecoder("utf-8").decode(t)),text:()=>{throw new Error("NotImplementedException")},headers:{get:()=>{}}})},e.pendingDownloadInternal.response}const t=e.loadRemote&&Pe.config.remoteSources?Pe.config.remoteSources:[""];let o;for(let n of t){n=n.trim(),"./"===n&&(n="");const t=le(e,n);e.name===t?Pe.diagnosticTracing&&b(`Attempting to download '${t}'`):Pe.diagnosticTracing&&b(`Attempting to download '${t}' for ${e.name}`);try{e.resolvedUrl=t;const n=fe(e);if(e.pendingDownloadInternal=n,o=await n.response,!o||!o.ok)continue;return o}catch(e){o||(o={ok:!1,url:t,status:0,statusText:""+e});continue}}const n=e.isOptional||e.name.match(/\.pdb$/)&&Pe.config.ignorePdbLoadErrors;if(o||Be(!1,`Response undefined ${e.name}`),!n){const t=new Error(`download '${o.url}' for ${e.name} failed ${o.status} ${o.statusText}`);throw t.status=o.status,t}y(`optional download '${o.url}' for ${e.name} failed ${o.status} ${o.statusText}`);}(e);return t?(J[e.behavior]||(e.buffer=await t.arrayBuffer(),++Pe.actual_downloaded_assets_count),e):e}finally{if(--N,L&&N==Pe.maxParallelDownloads-1){Pe.diagnosticTracing&&b("Resuming more parallel downloads");const e=L;L=void 0,e.promise_control.resolve();}}}function le(e,t){let o;return null==t&&Be(false,`sourcePrefix must be provided for ${e.name}`),e.resolvedUrl?o=e.resolvedUrl:(o=""===t?"assembly"===e.behavior||"pdb"===e.behavior?e.name:"resource"===e.behavior&&e.culture&&""!==e.culture?`${e.culture}/${e.name}`:e.name:t+e.name,o=ce(Pe.locateFile(o),e.behavior)),o&&"string"==typeof o||Be(false,"attemptUrl need to be path or url string"),o}function ce(e,t){return Pe.modulesUniqueQuery&&q[t]&&(e+=Pe.modulesUniqueQuery),e}let de=0;const ue=new Set;function fe(e){try{e.resolvedUrl||Be(!1,"Request's resolvedUrl must be set");const t=function(e){let t=e.resolvedUrl;if(Pe.loadBootResource){const o=ge(e);if(o instanceof Promise)return o;"string"==typeof o&&(t=o);}const o={};return e.cache?o.cache=e.cache:Pe.config.disableNoCacheFetch||(o.cache="no-cache"),e.useCredentials?o.credentials="include":!Pe.config.disableIntegrityCheck&&e.hash&&(o.integrity=e.hash),Pe.fetch_like(t,o)}(e),o={name:e.name,url:e.resolvedUrl,response:t};return ue.add(e.name),o.response.then((()=>{"assembly"==e.behavior&&Pe.loadedAssemblies.push(e.name),de++,Pe.onDownloadResourceProgress&&Pe.onDownloadResourceProgress(de,ue.size);})),o}catch(t){const o={ok:false,url:e.resolvedUrl,status:500,statusText:"ERR29: "+t,arrayBuffer:()=>{throw t},json:()=>{throw t}};return {name:e.name,url:e.resolvedUrl,response:Promise.resolve(o)}}}const me={resource:"assembly",assembly:"assembly",pdb:"pdb",icu:"globalization",vfs:"configuration",manifest:"manifest",dotnetwasm:"dotnetwasm","js-module-dotnet":"dotnetjs","js-module-native":"dotnetjs","js-module-runtime":"dotnetjs","js-module-threads":"dotnetjs"};function ge(e){var t;if(Pe.loadBootResource){const o=null!==(t=e.hash)&&void 0!==t?t:"",n=e.resolvedUrl,r=me[e.behavior];if(r){const t=Pe.loadBootResource(r,e.name,n,o,e.behavior);return "string"==typeof t?I(t):t}}}function pe(e){e.pendingDownloadInternal=null,e.pendingDownload=null,e.buffer=null,e.moduleExports=null;}function he(e){let t=e.lastIndexOf("/");return t>=0&&t++,e.substring(t)}async function we(e){e&&await Promise.all((null!=e?e:[]).map((e=>async function(e){try{const t=e.name;if(!e.moduleExports){const o=ce(Pe.locateFile(t),"js-module-library-initializer");Pe.diagnosticTracing&&b(`Attempting to import '${o}' for ${e}`),e.moduleExports=await import(((s) => typeof s === "string" && s[0] === "." && (s[1] === "/" || (s[1] === "." && s[2] === "/")) ? new URL(s, new URL("_framework/", __spawnjsBundleUrl).href).href : s)(/*! webpackIgnore: true */o));}Pe.libraryInitializers.push({scriptName:t,exports:e.moduleExports});}catch(t){E(`Failed to import library initializer '${e}': ${t}`);}}(e))));}async function be(e,t){if(!Pe.libraryInitializers)return;const o=[];for(let n=0;n<Pe.libraryInitializers.length;n++){const r=Pe.libraryInitializers[n];r.exports[e]&&o.push(ye(r.scriptName,e,(()=>r.exports[e](...t))));}await Promise.all(o);}async function ye(e,t,o){try{await o();}catch(o){throw E(`Failed to invoke '${t}' on library initializer '${e}': ${o}`),Xe(1,o),o}}function ve(e,t){if(e===t)return e;const o={...t};return void 0!==o.assets&&o.assets!==e.assets&&(o.assets=[...e.assets||[],...o.assets||[]]),void 0!==o.resources&&(o.resources=_e(e.resources||{assembly:[],jsModuleNative:[],jsModuleRuntime:[],wasmNative:[]},o.resources)),void 0!==o.environmentVariables&&(o.environmentVariables={...e.environmentVariables||{},...o.environmentVariables||{}}),void 0!==o.runtimeOptions&&o.runtimeOptions!==e.runtimeOptions&&(o.runtimeOptions=[...e.runtimeOptions||[],...o.runtimeOptions||[]]),Object.assign(e,o)}function Ee(e,t){if(e===t)return e;const o={...t};return o.config&&(e.config||(e.config={}),o.config=ve(e.config,o.config)),Object.assign(e,o)}function _e(e,t){if(e===t)return e;const o={...t};return void 0!==o.coreAssembly&&(o.coreAssembly=[...e.coreAssembly||[],...o.coreAssembly||[]]),void 0!==o.assembly&&(o.assembly=[...e.assembly||[],...o.assembly||[]]),void 0!==o.lazyAssembly&&(o.lazyAssembly=[...e.lazyAssembly||[],...o.lazyAssembly||[]]),void 0!==o.corePdb&&(o.corePdb=[...e.corePdb||[],...o.corePdb||[]]),void 0!==o.pdb&&(o.pdb=[...e.pdb||[],...o.pdb||[]]),void 0!==o.jsModuleWorker&&(o.jsModuleWorker=[...e.jsModuleWorker||[],...o.jsModuleWorker||[]]),void 0!==o.jsModuleNative&&(o.jsModuleNative=[...e.jsModuleNative||[],...o.jsModuleNative||[]]),void 0!==o.jsModuleDiagnostics&&(o.jsModuleDiagnostics=[...e.jsModuleDiagnostics||[],...o.jsModuleDiagnostics||[]]),void 0!==o.jsModuleRuntime&&(o.jsModuleRuntime=[...e.jsModuleRuntime||[],...o.jsModuleRuntime||[]]),void 0!==o.wasmSymbols&&(o.wasmSymbols=[...e.wasmSymbols||[],...o.wasmSymbols||[]]),void 0!==o.wasmNative&&(o.wasmNative=[...e.wasmNative||[],...o.wasmNative||[]]),void 0!==o.icu&&(o.icu=[...e.icu||[],...o.icu||[]]),void 0!==o.satelliteResources&&(o.satelliteResources=function(e,t){if(e===t)return e;for(const o in t)e[o]=[...e[o]||[],...t[o]||[]];return e}(e.satelliteResources||{},o.satelliteResources||{})),void 0!==o.modulesAfterConfigLoaded&&(o.modulesAfterConfigLoaded=[...e.modulesAfterConfigLoaded||[],...o.modulesAfterConfigLoaded||[]]),void 0!==o.modulesAfterRuntimeReady&&(o.modulesAfterRuntimeReady=[...e.modulesAfterRuntimeReady||[],...o.modulesAfterRuntimeReady||[]]),void 0!==o.extensions&&(o.extensions={...e.extensions||{},...o.extensions||{}}),void 0!==o.vfs&&(o.vfs=[...e.vfs||[],...o.vfs||[]]),Object.assign(e,o)}function xe(){const e=Pe.config;if(e.environmentVariables=e.environmentVariables||{},e.runtimeOptions=e.runtimeOptions||[],e.resources=e.resources||{assembly:[],jsModuleNative:[],jsModuleWorker:[],jsModuleRuntime:[],wasmNative:[],vfs:[],satelliteResources:{}},e.assets){Pe.diagnosticTracing&&b("config.assets is deprecated, use config.resources instead");for(const t of e.assets){const o={};switch(t.behavior){case "assembly":o.assembly=[t];break;case "pdb":o.pdb=[t];break;case "resource":o.satelliteResources={},o.satelliteResources[t.culture]=[t];break;case "icu":o.icu=[t];break;case "symbols":o.wasmSymbols=[t];break;case "vfs":o.vfs=[t];break;case "dotnetwasm":o.wasmNative=[t];break;case "js-module-threads":o.jsModuleWorker=[t];break;case "js-module-runtime":o.jsModuleRuntime=[t];break;case "js-module-native":o.jsModuleNative=[t];break;case "js-module-diagnostics":o.jsModuleDiagnostics=[t];break;case "js-module-dotnet":break;default:throw new Error(`Unexpected behavior ${t.behavior} of asset ${t.name}`)}_e(e.resources,o);}}e.debugLevel,e.applicationEnvironment||(e.applicationEnvironment="Production"),e.applicationCulture&&(e.environmentVariables.LANG=`${e.applicationCulture}.UTF-8`),Ue.diagnosticTracing=Pe.diagnosticTracing=!!e.diagnosticTracing,Ue.waitForDebugger=e.waitForDebugger,Pe.maxParallelDownloads=e.maxParallelDownloads||Pe.maxParallelDownloads,Pe.enableDownloadRetry=void 0!==e.enableDownloadRetry?e.enableDownloadRetry:Pe.enableDownloadRetry;}let je=false;async function Re(e){var t;if(je)return void await Pe.afterConfigLoaded.promise;let o;try{if(e.configSrc||Pe.config&&0!==Object.keys(Pe.config).length&&(Pe.config.assets||Pe.config.resources)||(e.configSrc="dotnet.boot.js"),o=e.configSrc,je=!0,o&&(Pe.diagnosticTracing&&b("mono_wasm_load_config"),await async function(e){const t=e.configSrc,o=Pe.locateFile(t);let n=null;void 0!==Pe.loadBootResource&&(n=Pe.loadBootResource("manifest",t,o,"","manifest"));let r,i=null;if(n)if("string"==typeof n)n.includes(".json")?(i=await s(I(n)),r=await Ae(i)):r=(await import(((s) => typeof s === "string" && s[0] === "." && (s[1] === "/" || (s[1] === "." && s[2] === "/")) ? new URL(s, new URL("_framework/", __spawnjsBundleUrl).href).href : s)(I(n)))).config;else {const e=await n;"function"==typeof e.json?(i=e,r=await Ae(i)):r=e.config;}else o.includes(".json")?(i=await s(ce(o,"manifest")),r=await Ae(i)):r=(await import(((s) => typeof s === "string" && s[0] === "." && (s[1] === "/" || (s[1] === "." && s[2] === "/")) ? new URL(s, new URL("_framework/", __spawnjsBundleUrl).href).href : s)(ce(o,"manifest")))).config;function s(e){return Pe.fetch_like(e,{method:"GET",credentials:"include",cache:"no-cache"})}Pe.config.applicationEnvironment&&(r.applicationEnvironment=Pe.config.applicationEnvironment),ve(Pe.config,r);}(e)),xe(),await we(null===(t=Pe.config.resources)||void 0===t?void 0:t.modulesAfterConfigLoaded),await be("onRuntimeConfigLoaded",[Pe.config]),e.onConfigLoaded)try{await e.onConfigLoaded(Pe.config,Le),xe();}catch(e){throw _("onConfigLoaded() failed",e),e}xe(),Pe.afterConfigLoaded.promise_control.resolve(Pe.config);}catch(t){const n=`Failed to load config file ${o} ${t} ${null==t?void 0:t.stack}`;throw Pe.config=e.config=Object.assign(Pe.config,{message:n,error:t,isError:true}),Xe(1,new Error(n)),t}}function Te(){return !!globalThis.navigator&&(Pe.isChromium||Pe.isFirefox)}async function Ae(e){const t=Pe.config,o=await e.json();t.applicationEnvironment||o.applicationEnvironment||(o.applicationEnvironment=e.headers.get("Blazor-Environment")||e.headers.get("DotNet-Environment")||void 0),o.environmentVariables||(o.environmentVariables={});const n=e.headers.get("DOTNET-MODIFIABLE-ASSEMBLIES");n&&(o.environmentVariables.DOTNET_MODIFIABLE_ASSEMBLIES=n);const r=e.headers.get("ASPNETCORE-BROWSER-TOOLS");return r&&(o.environmentVariables.__ASPNETCORE_BROWSER_TOOLS=r),o}"function"!=typeof importScripts||globalThis.onmessage||(globalThis.dotnetSidecar=true);const Se="object"==typeof process&&"object"==typeof process.versions&&"string"==typeof process.versions.node,De="function"==typeof importScripts,Oe=De&&"undefined"!=typeof dotnetSidecar,Ce=De&&!Oe,ke="object"==typeof window||De&&!Se,Ie=!ke&&!Se;let Ue={},Pe={},Me={},Le={},Ne={},$e=false;const ze={},We={config:ze},Fe={mono:{},binding:{},internal:Ne,module:We,loaderHelpers:Pe,runtimeHelpers:Ue,diagnosticHelpers:Me,api:Le};function Be(e,t){if(e)return;const o="Assert failed: "+("function"==typeof t?t():t),n=new Error(o);_(o,n),Ue.nativeAbort(n);}function Ve(){return void 0!==Pe.exitCode}function qe(){return Ue.runtimeReady&&!Ve()}function He(){Ve()&&Be(false,`.NET runtime already exited with ${Pe.exitCode} ${Pe.exitReason}. You can use runtime.runMain() which doesn't exit the runtime.`),Ue.runtimeReady||Be(false,".NET runtime didn't start yet. Please call dotnet.create() first.");}function Je(){ke&&(globalThis.addEventListener("unhandledrejection",et),globalThis.addEventListener("error",tt));}let Ze,Qe;function Ge(e){Qe&&Qe(e),Xe(e,Pe.exitReason);}function Ke(e){Ze&&Ze(e||Pe.exitReason),Xe(1,e||Pe.exitReason);}function Xe(t,o){var n,r;const i=o&&"object"==typeof o;t=i&&"number"==typeof o.status?o.status:void 0===t?-1:t;const s=i&&"string"==typeof o.message?o.message:""+o;(o=i?o:Ue.ExitStatus?function(e,t){const o=new Ue.ExitStatus(e);return o.message=t,o.toString=()=>t,o}(t,s):new Error("Exit with code "+t+" "+s)).status=t,o.message||(o.message=s);const a=""+(o.stack||(new Error).stack);try{Object.defineProperty(o,"stack",{get:()=>a});}catch(e){}const l=!!o.silent;if(o.silent=true,Ve())Pe.diagnosticTracing&&b("mono_exit called after exit");else {try{We.onAbort==Ke&&(We.onAbort=Ze),We.onExit==Ge&&(We.onExit=Qe),ke&&(globalThis.removeEventListener("unhandledrejection",et),globalThis.removeEventListener("error",tt)),Ue.runtimeReady?(Ue.jiterpreter_dump_stats&&Ue.jiterpreter_dump_stats(!1),0===t&&(null===(n=Pe.config)||void 0===n?void 0:n.interopCleanupOnExit)&&Ue.forceDisposeProxies(!0,!0),e&&0!==t&&(null===(r=Pe.config)||void 0===r||r.dumpThreadsOnNonZeroExit)):(Pe.diagnosticTracing&&b(`abort_startup, reason: ${o}`),function(e){Pe.allDownloadsQueued.promise_control.reject(e),Pe.allDownloadsFinished.promise_control.reject(e),Pe.afterConfigLoaded.promise_control.reject(e),Pe.wasmCompilePromise.promise_control.reject(e),Pe.runtimeModuleLoaded.promise_control.reject(e),Ue.dotnetReady&&(Ue.dotnetReady.promise_control.reject(e),Ue.afterInstantiateWasm.promise_control.reject(e),Ue.beforePreInit.promise_control.reject(e),Ue.afterPreInit.promise_control.reject(e),Ue.afterPreRun.promise_control.reject(e),Ue.beforeOnRuntimeInitialized.promise_control.reject(e),Ue.afterOnRuntimeInitialized.promise_control.reject(e),Ue.afterPostRun.promise_control.reject(e));}(o));}catch(e){E("mono_exit A failed",e);}try{l||(function(e,t){if(0!==e&&t){const e=Ue.ExitStatus&&t instanceof Ue.ExitStatus?b:_;"string"==typeof t?e(t):(void 0===t.stack&&(t.stack=(new Error).stack+""),t.message?e(Ue.stringify_as_error_with_stack?Ue.stringify_as_error_with_stack(t.message+"\n"+t.stack):t.message+"\n"+t.stack):e(JSON.stringify(t)));}!Ce&&Pe.config&&(Pe.config.logExitCode?Pe.config.forwardConsoleLogsToWS?R("WASM EXIT "+e):v("WASM EXIT "+e):Pe.config.forwardConsoleLogsToWS&&R());}(t,o),function(e){if(ke&&!Ce&&Pe.config&&Pe.config.appendElementOnExit&&document){const t=document.createElement("label");t.id="tests_done",0!==e&&(t.style.background="red"),t.innerHTML=""+e,document.body.appendChild(t);}}(t));}catch(e){E("mono_exit B failed",e);}Pe.exitCode=t,Pe.exitReason||(Pe.exitReason=o),!Ce&&Ue.runtimeReady&&We.runtimeKeepalivePop();}if(Pe.config&&Pe.config.asyncFlushOnExit&&0===t)throw (async()=>{try{await async function(){try{const e=await import(/*! webpackIgnore: true */'process'),t=e=>new Promise(((t,o)=>{e.on("error",o),e.end("","utf8",t);})),o=t(e.stderr),n=t(e.stdout);let r;const i=new Promise((e=>{r=setTimeout((()=>e("timeout")),1e3);}));await Promise.race([Promise.all([n,o]),i]),clearTimeout(r);}catch(e){_(`flushing std* streams failed: ${e}`);}}();}finally{Ye(t,o);}})(),o;Ye(t,o);}function Ye(e,t){if(Ue.runtimeReady&&Ue.nativeExit)try{Ue.nativeExit(e);}catch(e){!Ue.ExitStatus||e instanceof Ue.ExitStatus||E("set_exit_code_and_quit_now failed: "+e.toString());}if(0!==e||!ke)throw Se&&Ne.process?Ne.process.exit(e):Ue.quit&&Ue.quit(e,t),t}function et(e){ot(e,e.reason,"rejection");}function tt(e){ot(e,e.error,"error");}function ot(e,t,o){e.preventDefault();try{t||(t=new Error("Unhandled "+o)),void 0===t.stack&&(t.stack=(new Error).stack),t.stack=t.stack+"",t.silent||(_("Unhandled error:",t),Xe(1,t));}catch(e){}}!function(e){if($e)throw new Error("Loader module already loaded");$e=true,Ue=e.runtimeHelpers,Pe=e.loaderHelpers,Me=e.diagnosticHelpers,Le=e.api,Ne=e.internal,Object.assign(Le,{INTERNAL:Ne,invokeLibraryInitializers:be}),Object.assign(e.module,{config:ve(ze,{environmentVariables:{}})});const r={mono_wasm_bindings_is_ready:false,config:e.module.config,diagnosticTracing:false,nativeAbort:e=>{throw e||new Error("abort")},nativeExit:e=>{throw new Error("exit:"+e)}},l={gitHash:"e2f47b0110ed922f21a1522da67279133ce28f32",config:e.module.config,diagnosticTracing:false,maxParallelDownloads:16,enableDownloadRetry:true,_loaded_files:[],loadedFiles:[],loadedAssemblies:[],libraryInitializers:[],workerNextNumber:1,actual_downloaded_assets_count:0,actual_instantiated_assets_count:0,expected_downloaded_assets_count:0,expected_instantiated_assets_count:0,afterConfigLoaded:i(),allDownloadsQueued:i(),allDownloadsFinished:i(),wasmCompilePromise:i(),runtimeModuleLoaded:i(),loadingWorkers:i(),is_exited:Ve,is_runtime_running:qe,assert_runtime_running:He,mono_exit:Xe,createPromiseController:i,getPromiseController:s,assertIsControllablePromise:a,mono_download_assets:oe,resolve_single_asset_path:ee,setup_proxy_console:j,set_thread_prefix:w,installUnhandledErrorHandler:Je,retrieve_asset_download:ie,invokeLibraryInitializers:be,isDebuggingSupported:Te,exceptions:t,simd:n,relaxedSimd:o};Object.assign(Ue,r),Object.assign(Pe,l);}(Fe);let nt,rt,it,st=false,at=false;async function lt(e){if(!at){if(at=true,ke&&Pe.config.forwardConsoleLogsToWS&&void 0!==globalThis.WebSocket&&j("main",globalThis.console,globalThis.location.origin),We||Be(false,"Null moduleConfig"),Pe.config||Be(false,"Null moduleConfig.config"),"function"==typeof e){const t=e(Fe.api);if(t.ready)throw new Error("Module.ready couldn't be redefined.");Object.assign(We,t),Ee(We,t);}else {if("object"!=typeof e)throw new Error("Can't use moduleFactory callback of createDotnetRuntime function.");Ee(We,e);}await async function(e){if(Se){const e=await import(/*! webpackIgnore: true */'process'),t=14;if(e.versions.node.split(".")[0]<t)throw new Error(`NodeJS at '${e.execPath}' has too low version '${e.versions.node}', please use at least ${t}. See also https://aka.ms/dotnet-wasm-features`)}const t=/*! webpackIgnore: true */new URL("_framework/dotnet.js" + String(__spawnjsBundleUrl).replace(/^[^?#]*/, ''), __spawnjsBundleUrl).href,o=t.indexOf("?");var n;if(o>0&&(Pe.modulesUniqueQuery=t.substring(o)),Pe.scriptUrl=t.replace(/\\/g,"/").replace(/[?#].*/,""),Pe.scriptDirectory=(n=Pe.scriptUrl).slice(0,n.lastIndexOf("/"))+"/",Pe.locateFile=e=>"URL"in globalThis&&globalThis.URL!==C?new URL(e,Pe.scriptDirectory).toString():M(e)?e:Pe.scriptDirectory+e,Pe.fetch_like=k,Pe.out=console.log,Pe.err=console.error,Pe.onDownloadResourceProgress=e.onDownloadResourceProgress,ke&&globalThis.navigator){const e=globalThis.navigator,t=e.userAgentData&&e.userAgentData.brands;t&&t.length>0?Pe.isChromium=t.some((e=>"Google Chrome"===e.brand||"Microsoft Edge"===e.brand||"Chromium"===e.brand)):e.userAgent&&(Pe.isChromium=e.userAgent.includes("Chrome"),Pe.isFirefox=e.userAgent.includes("Firefox"));}Ne.require=Se?await import(/*! webpackIgnore: true */'module').then((e=>e.createRequire(/*! webpackIgnore: true */new URL("_framework/dotnet.js" + String(__spawnjsBundleUrl).replace(/^[^?#]*/, ''), __spawnjsBundleUrl).href))):Promise.resolve((()=>{throw new Error("require not supported")})),void 0===globalThis.URL&&(globalThis.URL=C);}(We);}}async function ct(e){return await lt(e),Ze=We.onAbort,Qe=We.onExit,We.onAbort=Ke,We.onExit=Ge,We.ENVIRONMENT_IS_PTHREAD?async function(){((function(){const e=new MessageChannel,t=e.port1,o=e.port2;t.addEventListener("message",(e=>{var n,r;n=JSON.parse(e.data.config),r=JSON.parse(e.data.monoThreadInfo),st?Pe.diagnosticTracing&&b("mono config already received"):(ve(Pe.config,n),Ue.monoThreadInfo=r,xe(),Pe.diagnosticTracing&&b("mono config received"),st=true,Pe.afterConfigLoaded.promise_control.resolve(Pe.config),ke&&n.forwardConsoleLogsToWS&&void 0!==globalThis.WebSocket&&Pe.setup_proxy_console("worker-idle",console,globalThis.location.origin)),t.close(),o.close();}),{once:true}),t.start(),self.postMessage({[l]:{monoCmd:"preload",port:o}},[o]);}))(),await Pe.afterConfigLoaded.promise,function(){const e=Pe.config;e.assets||Be(false,"config.assets must be defined");for(const t of e.assets)X(t),Q[t.behavior]&&z.push(t);}(),setTimeout((async()=>{try{await oe();}catch(e){Xe(1,e);}}),0);const e=dt(),t=await Promise.all(e);return await ut(t),We}():async function(){var e;await Re(We),re();const t=dt();((async function(){try{const e=ee("dotnetwasm");await se(e),e&&e.pendingDownloadInternal&&e.pendingDownloadInternal.response||Be(!1,"Can't load dotnet.native.wasm");const t=await e.pendingDownloadInternal.response,o=t.headers&&t.headers.get?t.headers.get("Content-Type"):void 0;let n;if("function"==typeof WebAssembly.compileStreaming&&"application/wasm"===o)n=await WebAssembly.compileStreaming(t);else {ke&&"application/wasm"!==o&&E('WebAssembly resource does not have the expected content type "application/wasm", so falling back to slower ArrayBuffer instantiation.');const e=await t.arrayBuffer();Pe.diagnosticTracing&&b("instantiate_wasm_module buffered"),n=Ie?await Promise.resolve(new WebAssembly.Module(e)):await WebAssembly.compile(e);}e.pendingDownloadInternal=null,e.pendingDownload=null,e.buffer=null,e.moduleExports=null,Pe.wasmCompilePromise.promise_control.resolve(n);}catch(e){Pe.wasmCompilePromise.promise_control.reject(e);}}))(),setTimeout((async()=>{try{D(),await oe();}catch(e){Xe(1,e);}}),0);const o=await Promise.all(t);return await ut(o),await Ue.dotnetReady.promise,await we(null===(e=Pe.config.resources)||void 0===e?void 0:e.modulesAfterRuntimeReady),await be("onRuntimeReady",[Fe.api]),Le}()}function dt(){const e=ee("js-module-runtime"),t=ee("js-module-native");if(nt&&rt)return [nt,rt,it];"object"==typeof e.moduleExports?nt=e.moduleExports:(Pe.diagnosticTracing&&b(`Attempting to import '${e.resolvedUrl}' for ${e.name}`),nt=import(((s) => typeof s === "string" && s[0] === "." && (s[1] === "/" || (s[1] === "." && s[2] === "/")) ? new URL(s, new URL("_framework/", __spawnjsBundleUrl).href).href : s)(/*! webpackIgnore: true */e.resolvedUrl))),"object"==typeof t.moduleExports?rt=t.moduleExports:(Pe.diagnosticTracing&&b(`Attempting to import '${t.resolvedUrl}' for ${t.name}`),rt=import(((s) => typeof s === "string" && s[0] === "." && (s[1] === "/" || (s[1] === "." && s[2] === "/")) ? new URL(s, new URL("_framework/", __spawnjsBundleUrl).href).href : s)(/*! webpackIgnore: true */t.resolvedUrl)));const o=Y("js-module-diagnostics");return o&&("object"==typeof o.moduleExports?it=o.moduleExports:(Pe.diagnosticTracing&&b(`Attempting to import '${o.resolvedUrl}' for ${o.name}`),it=import(((s) => typeof s === "string" && s[0] === "." && (s[1] === "/" || (s[1] === "." && s[2] === "/")) ? new URL(s, new URL("_framework/", __spawnjsBundleUrl).href).href : s)(/*! webpackIgnore: true */o.resolvedUrl)))),[nt,rt,it]}async function ut(e){const{initializeExports:t,initializeReplacements:o,configureRuntimeStartup:n,configureEmscriptenStartup:r,configureWorkerStartup:i,setRuntimeGlobals:s,passEmscriptenInternals:a}=e[0],{default:l}=e[1],c=e[2];s(Fe),t(Fe),c&&c.setRuntimeGlobals(Fe),await n(We),Pe.runtimeModuleLoaded.promise_control.resolve(),l((e=>(Object.assign(We,{ready:e.ready,__dotnet_runtime:{initializeReplacements:o,configureEmscriptenStartup:r,configureWorkerStartup:i,passEmscriptenInternals:a}}),We))).catch((e=>{if(e.message&&e.message.toLowerCase().includes("out of memory"))throw new Error(".NET runtime has failed to start, because too much memory was requested. Please decrease the memory by adjusting EmccMaximumHeapSize. See also https://aka.ms/dotnet-wasm-features");throw e}));}const ft=new class{withModuleConfig(e){try{return Ee(We,e),this}catch(e){throw Xe(1,e),e}}withOnConfigLoaded(e){try{return Ee(We,{onConfigLoaded:e}),this}catch(e){throw Xe(1,e),e}}withConsoleForwarding(){try{return ve(ze,{forwardConsoleLogsToWS:!0}),this}catch(e){throw Xe(1,e),e}}withExitOnUnhandledError(){try{return ve(ze,{exitOnUnhandledError:!0}),Je(),this}catch(e){throw Xe(1,e),e}}withAsyncFlushOnExit(){try{return ve(ze,{asyncFlushOnExit:!0}),this}catch(e){throw Xe(1,e),e}}withExitCodeLogging(){try{return ve(ze,{logExitCode:!0}),this}catch(e){throw Xe(1,e),e}}withElementOnExit(){try{return ve(ze,{appendElementOnExit:!0}),this}catch(e){throw Xe(1,e),e}}withInteropCleanupOnExit(){try{return ve(ze,{interopCleanupOnExit:!0}),this}catch(e){throw Xe(1,e),e}}withDumpThreadsOnNonZeroExit(){try{return ve(ze,{dumpThreadsOnNonZeroExit:!0}),this}catch(e){throw Xe(1,e),e}}withWaitingForDebugger(e){try{return ve(ze,{waitForDebugger:e}),this}catch(e){throw Xe(1,e),e}}withInterpreterPgo(e,t){try{return ve(ze,{interpreterPgo:e,interpreterPgoSaveDelay:t}),ze.runtimeOptions?ze.runtimeOptions.push("--interp-pgo-recording"):ze.runtimeOptions=["--interp-pgo-recording"],this}catch(e){throw Xe(1,e),e}}withConfig(e){try{return ve(ze,e),this}catch(e){throw Xe(1,e),e}}withConfigSrc(e){try{return e&&"string"==typeof e||Be(!1,"must be file path or URL"),Ee(We,{configSrc:e}),this}catch(e){throw Xe(1,e),e}}withVirtualWorkingDirectory(e){try{return e&&"string"==typeof e||Be(!1,"must be directory path"),ve(ze,{virtualWorkingDirectory:e}),this}catch(e){throw Xe(1,e),e}}withEnvironmentVariable(e,t){try{const o={};return o[e]=t,ve(ze,{environmentVariables:o}),this}catch(e){throw Xe(1,e),e}}withEnvironmentVariables(e){try{return e&&"object"==typeof e||Be(!1,"must be dictionary object"),ve(ze,{environmentVariables:e}),this}catch(e){throw Xe(1,e),e}}withDiagnosticTracing(e){try{return "boolean"!=typeof e&&Be(!1,"must be boolean"),ve(ze,{diagnosticTracing:e}),this}catch(e){throw Xe(1,e),e}}withDebugging(e){try{return null!=e&&"number"==typeof e||Be(!1,"must be number"),ve(ze,{debugLevel:e}),this}catch(e){throw Xe(1,e),e}}withApplicationArguments(...e){try{return e&&Array.isArray(e)||Be(!1,"must be array of strings"),ve(ze,{applicationArguments:e}),this}catch(e){throw Xe(1,e),e}}withRuntimeOptions(e){try{return e&&Array.isArray(e)||Be(!1,"must be array of strings"),ze.runtimeOptions?ze.runtimeOptions.push(...e):ze.runtimeOptions=e,this}catch(e){throw Xe(1,e),e}}withMainAssembly(e){try{return ve(ze,{mainAssemblyName:e}),this}catch(e){throw Xe(1,e),e}}withApplicationArgumentsFromQuery(){try{if(!globalThis.window)throw new Error("Missing window to the query parameters from");if(void 0===globalThis.URLSearchParams)throw new Error("URLSearchParams is supported");const e=new URLSearchParams(globalThis.window.location.search).getAll("arg");return this.withApplicationArguments(...e)}catch(e){throw Xe(1,e),e}}withApplicationEnvironment(e){try{return ve(ze,{applicationEnvironment:e}),this}catch(e){throw Xe(1,e),e}}withApplicationCulture(e){try{return ve(ze,{applicationCulture:e}),this}catch(e){throw Xe(1,e),e}}withResourceLoader(e){try{return Pe.loadBootResource=e,this}catch(e){throw Xe(1,e),e}}async download(){try{await async function(){lt(We),await Re(We),re(),D(),oe(),await Pe.allDownloadsFinished.promise;}();}catch(e){throw Xe(1,e),e}}async create(){try{return this.instance||(this.instance=await async function(){return await ct(We),Fe.api}()),this.instance}catch(e){throw Xe(1,e),e}}async run(){try{return We.config||Be(!1,"Null moduleConfig.config"),this.instance||await this.create(),this.instance.runMainAndExit()}catch(e){throw Xe(1,e),e}}};Ie||"function"==typeof globalThis.URL||Be(false,"This browser/engine doesn't support URL API. Please use a modern version. See also https://aka.ms/dotnet-wasm-features"),"function"!=typeof globalThis.BigInt64Array&&Be(false,"This browser/engine doesn't support BigInt64Array API. Please use a modern version. See also https://aka.ms/dotnet-wasm-features"),ft.withConfig(/*json-start*/{
      "mainAssemblyName": "SpawnDev.AI.Demo",
      "resources": {
        "hash": "sha256-8Cg89jzD555aDBXqoGhKvZGLwID8fOfpnckiSit919s=",
        "jsModuleNative": [
          {
            "name": "dotnet.native.smyhto3fbr.js",
            "moduleExports": dotnet_native_smyhto3fbr_js
          }
        ],
        "jsModuleRuntime": [
          {
            "name": "dotnet.runtime.zbexyp8zrs.js",
            "moduleExports": dotnet_runtime_zbexyp8zrs_js
          }
        ],
        "wasmNative": [
          {
            "name": "dotnet.native.miubzbpxmo.wasm",
            "hash": "sha256-wrF8C6d6DBHiiJ/qN6cWhe2JfNqrbKkDQIshlEkHIhA=",
            "resolvedUrl": dotnet_native_miubzbpxmo_wasm,
            "cache": "force-cache"
          }
        ],
        "icu": [
          {
            "virtualPath": "icudt_CJK.dat",
            "name": "icudt_CJK.tjcz0u77k5.dat",
            "hash": "sha256-SZLtQnRc0JkwqHab0VUVP7T3uBPSeYzxzDnpxPpUnHk=",
            "resolvedUrl": icudt_CJK_tjcz0u77k5_dat,
            "cache": "force-cache"
          },
          {
            "virtualPath": "icudt_EFIGS.dat",
            "name": "icudt_EFIGS.tptq2av103.dat",
            "hash": "sha256-8fItetYY8kQ0ww6oxwTLiT3oXlBwHKumbeP2pRF4yTc=",
            "resolvedUrl": icudt_EFIGS_tptq2av103_dat,
            "cache": "force-cache"
          },
          {
            "virtualPath": "icudt_no_CJK.dat",
            "name": "icudt_no_CJK.lfu7j35m59.dat",
            "hash": "sha256-L7sV7NEYP37/Qr2FPCePo5cJqRgTXRwGHuwF5Q+0Nfs=",
            "resolvedUrl": icudt_no_CJK_lfu7j35m59_dat,
            "cache": "force-cache"
          }
        ],
        "coreAssembly": [
          {
            "virtualPath": "System.Private.CoreLib.wasm",
            "name": "System.Private.CoreLib.acqhqif0l6.wasm",
            "hash": "sha256-Zo7KLnYA4xy8mxDzssMreXZfTlHBgRKnLWfYFMiQvCw=",
            "resolvedUrl": System_Private_CoreLib_acqhqif0l6_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Runtime.InteropServices.JavaScript.wasm",
            "name": "System.Runtime.InteropServices.JavaScript.11d6720mn8.wasm",
            "hash": "sha256-i0mCdwRLNy+fsELohj+iSdXW5CsN1dlkMsDHnTJw5sM=",
            "resolvedUrl": System_Runtime_InteropServices_JavaScript_11d6720mn8_wasm,
            "cache": "force-cache"
          }
        ],
        "assembly": [
          {
            "virtualPath": "BouncyCastle.Cryptography.wasm",
            "name": "BouncyCastle.Cryptography.6qnbenkd0k.wasm",
            "hash": "sha256-IqnVnhA0e0WF9/Ufx8/JIbwvVOGRIsbVT0pJuf86K70=",
            "resolvedUrl": BouncyCastle_Cryptography_6qnbenkd0k_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Concentus.wasm",
            "name": "Concentus.17t2e6w7vi.wasm",
            "hash": "sha256-s5l6Uv9Aan/iYIpJHfPkUYXHlRf4zqEmOx7I2oa+AkI=",
            "resolvedUrl": Concentus_17t2e6w7vi_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "DnsClient.wasm",
            "name": "DnsClient.7sic17ztch.wasm",
            "hash": "sha256-F3dZ3NYYdk5VvY3HUqpSuhvzGqynmxs+rkei8P2Svho=",
            "resolvedUrl": DnsClient_7sic17ztch_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "ILGPU.Algorithms.wasm",
            "name": "ILGPU.Algorithms.2m9vfxwo1w.wasm",
            "hash": "sha256-lirRwOfen2WIW0Uvqj+0z9K8tMVaRwGTJHxKheHHmtc=",
            "resolvedUrl": ILGPU_Algorithms_2m9vfxwo1w_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "ILGPU.wasm",
            "name": "ILGPU.zzpl2kir08.wasm",
            "hash": "sha256-32gnC+tVCB9MoSrIhAMjvGHsCXuPwhIGxFAZ1o1Fk2s=",
            "resolvedUrl": ILGPU_zzpl2kir08_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.AspNetCore.Authorization.wasm",
            "name": "Microsoft.AspNetCore.Authorization.4w3z20tuqk.wasm",
            "hash": "sha256-jG9J70J7TtOrMBWM7jjeQAS39juTl4bJpRXGN6fhQlg=",
            "resolvedUrl": Microsoft_AspNetCore_Authorization_4w3z20tuqk_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.AspNetCore.Components.wasm",
            "name": "Microsoft.AspNetCore.Components.0z8r7oen0q.wasm",
            "hash": "sha256-LM04i1jy+LgVHqU5bGAbOGfq3sokEYpY33rzzuaadlw=",
            "resolvedUrl": Microsoft_AspNetCore_Components_0z8r7oen0q_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.AspNetCore.Components.Forms.wasm",
            "name": "Microsoft.AspNetCore.Components.Forms.y5xx1fx6r9.wasm",
            "hash": "sha256-JvMWXAOhDPo+zB8yHSWOEHEeT9/fcwZCQDwAxCPHXlg=",
            "resolvedUrl": Microsoft_AspNetCore_Components_Forms_y5xx1fx6r9_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.AspNetCore.Components.Web.wasm",
            "name": "Microsoft.AspNetCore.Components.Web.aeyv0fw1i0.wasm",
            "hash": "sha256-HmTn6qpK4NWMdGwHU6HJ/nunn6y5o3uCm5V7+7M0480=",
            "resolvedUrl": Microsoft_AspNetCore_Components_Web_aeyv0fw1i0_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.AspNetCore.Components.WebAssembly.wasm",
            "name": "Microsoft.AspNetCore.Components.WebAssembly.9qaxkpf6y7.wasm",
            "hash": "sha256-b0NoLF7H21exT475itv370PzwHt299S+O7QLL9S0wPQ=",
            "resolvedUrl": Microsoft_AspNetCore_Components_WebAssembly_9qaxkpf6y7_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.AspNetCore.Metadata.wasm",
            "name": "Microsoft.AspNetCore.Metadata.s8loo8n9nc.wasm",
            "hash": "sha256-dJZQlLCWV8HxVn+VLwiM3zh+2v2/BoHWNtaWvNqMdS8=",
            "resolvedUrl": Microsoft_AspNetCore_Metadata_s8loo8n9nc_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.CSharp.wasm",
            "name": "Microsoft.CSharp.vc74ka0j2f.wasm",
            "hash": "sha256-P7qKvEHMowsS2wvBLOsl84cAPOho5yGcSqn3c4RfAWs=",
            "resolvedUrl": Microsoft_CSharp_vc74ka0j2f_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.Extensions.Configuration.Abstractions.wasm",
            "name": "Microsoft.Extensions.Configuration.Abstractions.iqery9nry2.wasm",
            "hash": "sha256-Wtkv6KZJ6c4+tzKrdRIJ2f3EhIakYPDfAn2Rj04zHR0=",
            "resolvedUrl": Microsoft_Extensions_Configuration_Abstractions_iqery9nry2_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.Extensions.Configuration.Binder.wasm",
            "name": "Microsoft.Extensions.Configuration.Binder.7ebfchnp6m.wasm",
            "hash": "sha256-bwcA9NPW/7dv3k+Mr4N+gcZdNUYUtuUj3+O8YHwkbMc=",
            "resolvedUrl": Microsoft_Extensions_Configuration_Binder_7ebfchnp6m_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.Extensions.Configuration.FileExtensions.wasm",
            "name": "Microsoft.Extensions.Configuration.FileExtensions.phra2kqghi.wasm",
            "hash": "sha256-rVukgkTLSTNrk72xnW1GjiqXFDoZoeYNJz1CWkpzjqQ=",
            "resolvedUrl": Microsoft_Extensions_Configuration_FileExtensions_phra2kqghi_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.Extensions.Configuration.Json.wasm",
            "name": "Microsoft.Extensions.Configuration.Json.9udmvturlf.wasm",
            "hash": "sha256-kaJEtYTmGmQJ235nthDFz+Ang6Ruk+JnGVFKh9ULJqw=",
            "resolvedUrl": Microsoft_Extensions_Configuration_Json_9udmvturlf_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.Extensions.Configuration.wasm",
            "name": "Microsoft.Extensions.Configuration.qjwtydrbeh.wasm",
            "hash": "sha256-+o/D7fr2KXPZyZmjiCQLWPHVEh/oiumh8cIcUUlXN4U=",
            "resolvedUrl": Microsoft_Extensions_Configuration_qjwtydrbeh_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.Extensions.DependencyInjection.wasm",
            "name": "Microsoft.Extensions.DependencyInjection.0lh9y2h2sj.wasm",
            "hash": "sha256-RPtn6pkmjQgSJvC2r9Gf9YPVYplQr2s1x/1CuqngAo0=",
            "resolvedUrl": Microsoft_Extensions_DependencyInjection_0lh9y2h2sj_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.Extensions.DependencyInjection.Abstractions.wasm",
            "name": "Microsoft.Extensions.DependencyInjection.Abstractions.r4njuum1at.wasm",
            "hash": "sha256-27EFRDR3xbRa0nWlwVoIj7DmFf0h6tMjjYcNWEjPkNM=",
            "resolvedUrl": Microsoft_Extensions_DependencyInjection_Abstractions_r4njuum1at_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.Extensions.Diagnostics.Abstractions.wasm",
            "name": "Microsoft.Extensions.Diagnostics.Abstractions.k7gpa90haf.wasm",
            "hash": "sha256-UGy6L0YpW2Jk6nCcbqyaTZ10KREZolKUCPnZP73JBSc=",
            "resolvedUrl": Microsoft_Extensions_Diagnostics_Abstractions_k7gpa90haf_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.Extensions.Diagnostics.wasm",
            "name": "Microsoft.Extensions.Diagnostics.e4xkfi1ghr.wasm",
            "hash": "sha256-UhSR/+KrqvW8cRihNiuuZKZub5H7Bi6Uu5hn74ct0ok=",
            "resolvedUrl": Microsoft_Extensions_Diagnostics_e4xkfi1ghr_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.Extensions.FileProviders.Abstractions.wasm",
            "name": "Microsoft.Extensions.FileProviders.Abstractions.wbhloeatw3.wasm",
            "hash": "sha256-BMXU3fj0UK/6PNGpPF3cojEo3DozPCjJGaTZP6GoKdY=",
            "resolvedUrl": Microsoft_Extensions_FileProviders_Abstractions_wbhloeatw3_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.Extensions.FileProviders.Physical.wasm",
            "name": "Microsoft.Extensions.FileProviders.Physical.y5k56zvg10.wasm",
            "hash": "sha256-quilEO0GpuPvxSG6rAylSWEh5TwoCL+XbWNLelEOtds=",
            "resolvedUrl": Microsoft_Extensions_FileProviders_Physical_y5k56zvg10_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.Extensions.FileSystemGlobbing.wasm",
            "name": "Microsoft.Extensions.FileSystemGlobbing.4cqycihgg0.wasm",
            "hash": "sha256-1N2/rCQl/eAmYr1rfjJ32aMFRrX+RN35cCdm1VkbWGo=",
            "resolvedUrl": Microsoft_Extensions_FileSystemGlobbing_4cqycihgg0_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.Extensions.Logging.wasm",
            "name": "Microsoft.Extensions.Logging.05o184w673.wasm",
            "hash": "sha256-dEjzt1uKX3R6EmoJQrRj6zcO9nfGtBCBS4OOMUzbXIg=",
            "resolvedUrl": Microsoft_Extensions_Logging_05o184w673_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.Extensions.Logging.Abstractions.wasm",
            "name": "Microsoft.Extensions.Logging.Abstractions.ef2lrhidbn.wasm",
            "hash": "sha256-ij5nuTCdqyB4gAlMGyLXxnmohZYnn3SC233Zqd40tYU=",
            "resolvedUrl": Microsoft_Extensions_Logging_Abstractions_ef2lrhidbn_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.Extensions.Options.ConfigurationExtensions.wasm",
            "name": "Microsoft.Extensions.Options.ConfigurationExtensions.cbvevgdsho.wasm",
            "hash": "sha256-+No81T5HXezhQaZ4c1145qD3lvRa4r9Q3FWXw6ZfgAg=",
            "resolvedUrl": Microsoft_Extensions_Options_ConfigurationExtensions_cbvevgdsho_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.Extensions.Options.wasm",
            "name": "Microsoft.Extensions.Options.llivjk1whj.wasm",
            "hash": "sha256-6ULG5niYM+MHArol/TOSkMbfxTc7D5jKcorcHt/RPo0=",
            "resolvedUrl": Microsoft_Extensions_Options_llivjk1whj_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.Extensions.Primitives.wasm",
            "name": "Microsoft.Extensions.Primitives.ndur9bx6x9.wasm",
            "hash": "sha256-ixdbQi4Qj2Bd8xbpEM/764LZNO1roqKfS/hVzZiePNc=",
            "resolvedUrl": Microsoft_Extensions_Primitives_ndur9bx6x9_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.Extensions.Validation.wasm",
            "name": "Microsoft.Extensions.Validation.w1rf8w9vwy.wasm",
            "hash": "sha256-TPHp2vON5qjVdsb6ZuDh0rxu/FCFbB3r2IMcQHThpfo=",
            "resolvedUrl": Microsoft_Extensions_Validation_w1rf8w9vwy_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.JSInterop.WebAssembly.wasm",
            "name": "Microsoft.JSInterop.WebAssembly.9udcqny0aq.wasm",
            "hash": "sha256-EQfaQose2do/RGDJFP6YWTTeOWA2gufnJurZZZh2XLU=",
            "resolvedUrl": Microsoft_JSInterop_WebAssembly_9udcqny0aq_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.JSInterop.wasm",
            "name": "Microsoft.JSInterop.r7i6scgm0q.wasm",
            "hash": "sha256-SZIr25HLqaeri9f5UuJpYtWWdLH4iSpFr5RmCLj3CAI=",
            "resolvedUrl": Microsoft_JSInterop_r7i6scgm0q_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.VisualBasic.Core.wasm",
            "name": "Microsoft.VisualBasic.Core.ic4oks67r5.wasm",
            "hash": "sha256-AtC400vi85AY2+3JEyu7M0FZmzy5W/qPTQwnEYgE5WU=",
            "resolvedUrl": Microsoft_VisualBasic_Core_ic4oks67r5_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.VisualBasic.wasm",
            "name": "Microsoft.VisualBasic.oawltawhmj.wasm",
            "hash": "sha256-QIQIVsgVtOjss3PhGWReXuYJqZfXWjj4Ka9g42s9/kM=",
            "resolvedUrl": Microsoft_VisualBasic_oawltawhmj_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.Win32.Primitives.wasm",
            "name": "Microsoft.Win32.Primitives.n6ylnc1rdo.wasm",
            "hash": "sha256-sWj2rJGGR2WaxEbYR4rTRr70SK107Yxg07dskgAB9Gs=",
            "resolvedUrl": Microsoft_Win32_Primitives_n6ylnc1rdo_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "Microsoft.Win32.Registry.wasm",
            "name": "Microsoft.Win32.Registry.205yp9wx9t.wasm",
            "hash": "sha256-LvXRTg75x2rR8pl0Bw85waJhB6rLFe8JUXJZHQVj1AA=",
            "resolvedUrl": Microsoft_Win32_Registry_205yp9wx9t_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "SIPSorcery.wasm",
            "name": "SIPSorcery.5325rd4hw5.wasm",
            "hash": "sha256-L5eL/eHwxN7vn8uXlvrFfwGpJYiPhiaXdNuk7AOcroM=",
            "resolvedUrl": SIPSorcery_5325rd4hw5_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "SIPSorceryMedia.Abstractions.wasm",
            "name": "SIPSorceryMedia.Abstractions.evfg4tvmuu.wasm",
            "hash": "sha256-psMWDnBifjyCcYC5pnkGJ4xTqBfUANlbT0BNmhlTU8I=",
            "resolvedUrl": SIPSorceryMedia_Abstractions_evfg4tvmuu_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "SpawnDev.AI.wasm",
            "name": "SpawnDev.AI.5qmsvo1th1.wasm",
            "hash": "sha256-824w/7UfFnv2+Lh6GuJuHhOr/Rl68jNnYFeuQArDGIA=",
            "resolvedUrl": SpawnDev_AI_5qmsvo1th1_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "SpawnDev.AI.Demo.wasm",
            "name": "SpawnDev.AI.Demo.o6pspucbls.wasm",
            "hash": "sha256-gN/aC8Za61NuJ0twEkHpBp/AK51Zr10Rb85O0R/TAKI=",
            "resolvedUrl": SpawnDev_AI_Demo_o6pspucbls_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "SpawnDev.AI.Server.wasm",
            "name": "SpawnDev.AI.Server.3pyra3g39w.wasm",
            "hash": "sha256-Z95IJoR+KlssSK26kVgHm+C1Y/WvrUDRGBE1WpvLpYY=",
            "resolvedUrl": SpawnDev_AI_Server_3pyra3g39w_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "SpawnDev.AsyncFileSystem.wasm",
            "name": "SpawnDev.AsyncFileSystem.az5z799cdp.wasm",
            "hash": "sha256-+juwgN4NMevFhK2XcT58eOFL7YITtPSvw2UFhZYkqO4=",
            "resolvedUrl": SpawnDev_AsyncFileSystem_az5z799cdp_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "SpawnDev.BackgroundServices.wasm",
            "name": "SpawnDev.BackgroundServices.1yeryblw74.wasm",
            "hash": "sha256-l700IkrNgn3VOTRpLDeYJmH14vPVY1zHyc320gtyX7I=",
            "resolvedUrl": SpawnDev_BackgroundServices_1yeryblw74_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "SpawnDev.ILGPU.wasm",
            "name": "SpawnDev.ILGPU.51q19yjdic.wasm",
            "hash": "sha256-CRsb4fE9FjARcmT7x9BNhlS+aZd3chYBwXndz1Tt7ng=",
            "resolvedUrl": SpawnDev_ILGPU_51q19yjdic_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "SpawnDev.ILGPU.ML.wasm",
            "name": "SpawnDev.ILGPU.ML.ay4mxvr7ji.wasm",
            "hash": "sha256-ks88XkW4ITcFf5zidrbPE7XHY9tUua1o71Q/MR0PvFA=",
            "resolvedUrl": SpawnDev_ILGPU_ML_ay4mxvr7ji_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "SpawnDev.MultiMedia.wasm",
            "name": "SpawnDev.MultiMedia.hz2005i8oz.wasm",
            "hash": "sha256-HWMaOs6YhLcIdQt4UIcQkltaW6VC05DBuGuFgIIVwKE=",
            "resolvedUrl": SpawnDev_MultiMedia_hz2005i8oz_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "SpawnDev.Phonemizer.wasm",
            "name": "SpawnDev.Phonemizer.1qvgtwealf.wasm",
            "hash": "sha256-Z97VrTOE2GCr1/4bGsgSYTCs38ejtduJPbZCOlFOq/o=",
            "resolvedUrl": SpawnDev_Phonemizer_1qvgtwealf_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "SpawnDev.RTC.wasm",
            "name": "SpawnDev.RTC.j6o696q84t.wasm",
            "hash": "sha256-6+C8qR6c+uBhtHrXM95VlBXOhTH+5AkA5OFCvxHRKHQ=",
            "resolvedUrl": SpawnDev_RTC_j6o696q84t_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "SpawnDev.SpawnJS.wasm",
            "name": "SpawnDev.SpawnJS.42t7ugqy4h.wasm",
            "hash": "sha256-JDM7U8qmHeHrq6wCxioRxBbY6ueoBm6K5+Olff7DCII=",
            "resolvedUrl": SpawnDev_SpawnJS_42t7ugqy4h_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "SpawnDev.SpawnJS.Blazor.wasm",
            "name": "SpawnDev.SpawnJS.Blazor.35062n6gab.wasm",
            "hash": "sha256-yVsvyIafGqm9+BVd8Fw4y8Kfif08loza4+b63MRXXrA=",
            "resolvedUrl": SpawnDev_SpawnJS_Blazor_35062n6gab_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "SpawnDev.SpawnJS.Cryptography.wasm",
            "name": "SpawnDev.SpawnJS.Cryptography.u5p38diicb.wasm",
            "hash": "sha256-Ip7xQ0vdp0lHtHbjTkunuBK3gD0B+wsUpTtJIPjDD50=",
            "resolvedUrl": SpawnDev_SpawnJS_Cryptography_u5p38diicb_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "SpawnDev.SpawnJS.RazorRenderer.wasm",
            "name": "SpawnDev.SpawnJS.RazorRenderer.vtkxzi8wgc.wasm",
            "hash": "sha256-TQ0nza2z/BPMKuNLJC/AiWmlfBXJ1UAtDutJ/RThrgU=",
            "resolvedUrl": SpawnDev_SpawnJS_RazorRenderer_vtkxzi8wgc_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "SpawnDev.SpawnJS.WebWorkers.wasm",
            "name": "SpawnDev.SpawnJS.WebWorkers.o4bm440cce.wasm",
            "hash": "sha256-KAoAFfjAE+GtBfbeuObpRXeB3ob8IyszL7iFRSuHuJw=",
            "resolvedUrl": SpawnDev_SpawnJS_WebWorkers_o4bm440cce_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "SpawnDev.WebTorrent.wasm",
            "name": "SpawnDev.WebTorrent.715mc8duzo.wasm",
            "hash": "sha256-+74aCmExUk0mSIRgAp0sIqKgW6mxXkTmOGdk7GFbgYY=",
            "resolvedUrl": SpawnDev_WebTorrent_715mc8duzo_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.AppContext.wasm",
            "name": "System.AppContext.33lgp2bflw.wasm",
            "hash": "sha256-vz/SY7IT6HI0fgNQ8GZWZ+2a6G0TdNW8NiXfBQzCDFg=",
            "resolvedUrl": System_AppContext_33lgp2bflw_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Buffers.wasm",
            "name": "System.Buffers.g7kq07we8q.wasm",
            "hash": "sha256-NBuJlJA0h3fyxW5UP4hoYInaEwe9OZxG2gEbjIdmCrY=",
            "resolvedUrl": System_Buffers_g7kq07we8q_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Collections.Concurrent.wasm",
            "name": "System.Collections.Concurrent.ayt23dc2he.wasm",
            "hash": "sha256-IntDEoqraHR5cfj4BngQXlQjjlrcOYRCbXuQtRi06AY=",
            "resolvedUrl": System_Collections_Concurrent_ayt23dc2he_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Collections.Immutable.wasm",
            "name": "System.Collections.Immutable.9nsqwhhu4t.wasm",
            "hash": "sha256-haf/04roddsE9PJPtNQ70GK4E5Pgdvs8Xn7GWtwiB8E=",
            "resolvedUrl": System_Collections_Immutable_9nsqwhhu4t_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Collections.NonGeneric.wasm",
            "name": "System.Collections.NonGeneric.525tw7pi7d.wasm",
            "hash": "sha256-3Wp37kJjaV1lPseMx3LiVnPOb6jBbwgsqYQ5XifQfcI=",
            "resolvedUrl": System_Collections_NonGeneric_525tw7pi7d_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Collections.Specialized.wasm",
            "name": "System.Collections.Specialized.33r4c9ewf6.wasm",
            "hash": "sha256-H5J4UE+YDKwGT/+7eJ04nYWti5iFiGRcy+qc+mQi/O8=",
            "resolvedUrl": System_Collections_Specialized_33r4c9ewf6_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Collections.wasm",
            "name": "System.Collections.gtdrkq93la.wasm",
            "hash": "sha256-TPWn1Sks7F7hYJ6yIoiF5E16QgcubEClSpnYJRxXog8=",
            "resolvedUrl": System_Collections_gtdrkq93la_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.ComponentModel.Annotations.wasm",
            "name": "System.ComponentModel.Annotations.sojjklgz5l.wasm",
            "hash": "sha256-lE1lvR0jcFeSBLguBCsrgQ6E/SwzEWSBo6DT1t+eyzE=",
            "resolvedUrl": System_ComponentModel_Annotations_sojjklgz5l_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.ComponentModel.DataAnnotations.wasm",
            "name": "System.ComponentModel.DataAnnotations.q8seqyww2r.wasm",
            "hash": "sha256-hlc54d9ROri0ue7K99r10i4zkATiLhcQ9zo0q7W2hR4=",
            "resolvedUrl": System_ComponentModel_DataAnnotations_q8seqyww2r_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.ComponentModel.EventBasedAsync.wasm",
            "name": "System.ComponentModel.EventBasedAsync.7b6lvtlm4f.wasm",
            "hash": "sha256-M/bTWUvIaq4cjgVjpzDowRr/CILOEX/94pdcYydLVIk=",
            "resolvedUrl": System_ComponentModel_EventBasedAsync_7b6lvtlm4f_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.ComponentModel.Primitives.wasm",
            "name": "System.ComponentModel.Primitives.nwih63pj9c.wasm",
            "hash": "sha256-93uWz5htHU4mW5eFYLizhbEpyIP7wRw7m5BxQpDfSkA=",
            "resolvedUrl": System_ComponentModel_Primitives_nwih63pj9c_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.ComponentModel.TypeConverter.wasm",
            "name": "System.ComponentModel.TypeConverter.gq1uo9pwbw.wasm",
            "hash": "sha256-uC84qHy9xx+jXRXst6IaM1xt/w7LxUiXTeriFG4ZAiA=",
            "resolvedUrl": System_ComponentModel_TypeConverter_gq1uo9pwbw_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.ComponentModel.wasm",
            "name": "System.ComponentModel.gtzdp6gjij.wasm",
            "hash": "sha256-lEd4E48MsU5w1IehTBnjkEI2vNCXvuFPvPeMTyw3++U=",
            "resolvedUrl": System_ComponentModel_gtzdp6gjij_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Configuration.wasm",
            "name": "System.Configuration.q733dci1qs.wasm",
            "hash": "sha256-BnBG27W6TaVX+iCWFXq00o8F7GIIuUQtO0HkkMmwjMU=",
            "resolvedUrl": System_Configuration_q733dci1qs_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Console.wasm",
            "name": "System.Console.to0mbsovya.wasm",
            "hash": "sha256-AzhPOHmII8SXRU+N5orETTSLHfAesDlXNBoICnlf/hw=",
            "resolvedUrl": System_Console_to0mbsovya_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Core.wasm",
            "name": "System.Core.azclj5c5yv.wasm",
            "hash": "sha256-CqRaluF8Fsi/jz9kCwltS3ZZ2H/dItSmY+6I8eMXh+4=",
            "resolvedUrl": System_Core_azclj5c5yv_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Data.Common.wasm",
            "name": "System.Data.Common.nthckp9a3t.wasm",
            "hash": "sha256-O41AktxbWuV1VI6Jpdpn/SVaBfffTI9wPfARobgjWZ4=",
            "resolvedUrl": System_Data_Common_nthckp9a3t_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Data.DataSetExtensions.wasm",
            "name": "System.Data.DataSetExtensions.a9dlaxjhfy.wasm",
            "hash": "sha256-ouu5ej3421SgXUzAYf44bxCQ7RUxrfwqwgmEwvh6k6A=",
            "resolvedUrl": System_Data_DataSetExtensions_a9dlaxjhfy_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Data.wasm",
            "name": "System.Data.whqnroy0hk.wasm",
            "hash": "sha256-5GA1j3GuS3Q/muwMulPxIEys5V8jEFaVCDIho/UzAZ4=",
            "resolvedUrl": System_Data_whqnroy0hk_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Diagnostics.Contracts.wasm",
            "name": "System.Diagnostics.Contracts.1ocyilnve1.wasm",
            "hash": "sha256-X5WU0oWl2MmZG7InLJHAf9o6jmX4ihpQpuZh/VxktlM=",
            "resolvedUrl": System_Diagnostics_Contracts_1ocyilnve1_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Diagnostics.Debug.wasm",
            "name": "System.Diagnostics.Debug.0akkuosl4u.wasm",
            "hash": "sha256-qOE7MrpIyjV9RclZShTpxbW0dXN9NSp6E8O6BCM4j/k=",
            "resolvedUrl": System_Diagnostics_Debug_0akkuosl4u_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Diagnostics.DiagnosticSource.wasm",
            "name": "System.Diagnostics.DiagnosticSource.us3v4sw3re.wasm",
            "hash": "sha256-Aqcp7AQ4SF2SidBLhSXGiBMx6wUGEmRGMUGtJuw5Biw=",
            "resolvedUrl": System_Diagnostics_DiagnosticSource_us3v4sw3re_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Diagnostics.FileVersionInfo.wasm",
            "name": "System.Diagnostics.FileVersionInfo.52zf5lzwq0.wasm",
            "hash": "sha256-PShcf3L0QvoHeHX6WO3KQqAuMpM1dUIF3bOHwdvM02k=",
            "resolvedUrl": System_Diagnostics_FileVersionInfo_52zf5lzwq0_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Diagnostics.Process.wasm",
            "name": "System.Diagnostics.Process.aksc3z10sf.wasm",
            "hash": "sha256-mnphv8xnpoUBaUm9xj1BKdq1ZtrmXgfNC1pTeUX8Z1A=",
            "resolvedUrl": System_Diagnostics_Process_aksc3z10sf_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Diagnostics.StackTrace.wasm",
            "name": "System.Diagnostics.StackTrace.oyhnn763kn.wasm",
            "hash": "sha256-sM2dU4tKknoFqZJcmyFk/cQpUQVCrmiXLAqIvXTeHB4=",
            "resolvedUrl": System_Diagnostics_StackTrace_oyhnn763kn_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Diagnostics.TextWriterTraceListener.wasm",
            "name": "System.Diagnostics.TextWriterTraceListener.4dqf2t866y.wasm",
            "hash": "sha256-OAmvIaAFxlBfLbdbPMsrpB3c+g8LVN0zOy07o5j5Flw=",
            "resolvedUrl": System_Diagnostics_TextWriterTraceListener_4dqf2t866y_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Diagnostics.Tools.wasm",
            "name": "System.Diagnostics.Tools.4jmuc696pu.wasm",
            "hash": "sha256-cL64WwiWGdmdH0weZWJRZHKU7pWvR4JGCYHlGJGMroQ=",
            "resolvedUrl": System_Diagnostics_Tools_4jmuc696pu_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Diagnostics.TraceSource.wasm",
            "name": "System.Diagnostics.TraceSource.4djwg1x5dk.wasm",
            "hash": "sha256-+NGeFhqlXf6aE9U55il2yFOG92I2uTAw9V9l8WCD8+M=",
            "resolvedUrl": System_Diagnostics_TraceSource_4djwg1x5dk_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Diagnostics.Tracing.wasm",
            "name": "System.Diagnostics.Tracing.6xw2h3mehq.wasm",
            "hash": "sha256-KpCxa5K0+bU0W8TPjz8oJwyGdCZpA5ybxcUoEKh1zoA=",
            "resolvedUrl": System_Diagnostics_Tracing_6xw2h3mehq_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Drawing.Primitives.wasm",
            "name": "System.Drawing.Primitives.fht3cwi9ck.wasm",
            "hash": "sha256-/dvJgFicq8TLI4i4VdtMFPN1nKxit3i+vSbzahAe1cs=",
            "resolvedUrl": System_Drawing_Primitives_fht3cwi9ck_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Drawing.wasm",
            "name": "System.Drawing.cby5s9a0on.wasm",
            "hash": "sha256-+Dy89WFcA7dUgn0g1UJ8TjdZZQ6V8ZN0kZfrRA8xGJ4=",
            "resolvedUrl": System_Drawing_cby5s9a0on_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Dynamic.Runtime.wasm",
            "name": "System.Dynamic.Runtime.yi4mxor0by.wasm",
            "hash": "sha256-lm4ZND3nHWnLXcMzRKXz3I2BGdWPik99ttzib2RVCpw=",
            "resolvedUrl": System_Dynamic_Runtime_yi4mxor0by_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Formats.Asn1.wasm",
            "name": "System.Formats.Asn1.56jjdsv3d0.wasm",
            "hash": "sha256-M0vQyUCJLFCrFQX/Bg7bPG8nfXfxjvEYTseZkbfGYxg=",
            "resolvedUrl": System_Formats_Asn1_56jjdsv3d0_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Formats.Tar.wasm",
            "name": "System.Formats.Tar.89bpoq7vrz.wasm",
            "hash": "sha256-xAroer71Mir0OA4S5cPejkZaL03TqTFg1aimzQ9LIok=",
            "resolvedUrl": System_Formats_Tar_89bpoq7vrz_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Globalization.Calendars.wasm",
            "name": "System.Globalization.Calendars.rildgn7mzz.wasm",
            "hash": "sha256-zZUFwm7oGVSq1qsK8oIZuzSLLXjmcoLgT1iy5qqXHp8=",
            "resolvedUrl": System_Globalization_Calendars_rildgn7mzz_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Globalization.Extensions.wasm",
            "name": "System.Globalization.Extensions.opr37tm2pn.wasm",
            "hash": "sha256-9HWY1aCFc5GoQcysEA0v85vU5i8CRuqZvKqJJpzka6k=",
            "resolvedUrl": System_Globalization_Extensions_opr37tm2pn_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Globalization.wasm",
            "name": "System.Globalization.moqtkrs3kx.wasm",
            "hash": "sha256-qnMi2/9zgAiBieR/7gLp8ZIASS2EbKk5F3FK/ZFHXn8=",
            "resolvedUrl": System_Globalization_moqtkrs3kx_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.IO.Compression.wasm",
            "name": "System.IO.Compression.6pdq5v22pd.wasm",
            "hash": "sha256-2mhUJF3wl9QgdsvbtOy/ZDArJeyj8crmASfCqaGAtNw=",
            "resolvedUrl": System_IO_Compression_6pdq5v22pd_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.IO.Compression.Brotli.wasm",
            "name": "System.IO.Compression.Brotli.7ohel80nih.wasm",
            "hash": "sha256-CZooa2KeN4jmRIpQK41QSkQP6d4dmj++PSm0FlRGkuo=",
            "resolvedUrl": System_IO_Compression_Brotli_7ohel80nih_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.IO.Compression.FileSystem.wasm",
            "name": "System.IO.Compression.FileSystem.y83oisb571.wasm",
            "hash": "sha256-clgAvPC5qyl4EnAE8n7NPNbEX9+Y4Rpxpnxl5349y+4=",
            "resolvedUrl": System_IO_Compression_FileSystem_y83oisb571_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.IO.Compression.ZipFile.wasm",
            "name": "System.IO.Compression.ZipFile.tmxcz8z1z1.wasm",
            "hash": "sha256-uzfss5y8PFTeOVFAKk+jQLi/FLPKrehaUq6f+1xqeoQ=",
            "resolvedUrl": System_IO_Compression_ZipFile_tmxcz8z1z1_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.IO.FileSystem.AccessControl.wasm",
            "name": "System.IO.FileSystem.AccessControl.xzbtcseu3i.wasm",
            "hash": "sha256-A0JUayiM8+PP3INOm0TFut8IWKzKDe4PcEZGksJchHU=",
            "resolvedUrl": System_IO_FileSystem_AccessControl_xzbtcseu3i_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.IO.FileSystem.DriveInfo.wasm",
            "name": "System.IO.FileSystem.DriveInfo.g3te4cd6dq.wasm",
            "hash": "sha256-NPFQq5Ilvv6t6twDWNjv6zmCVVMv4GREj5xvBqMG71g=",
            "resolvedUrl": System_IO_FileSystem_DriveInfo_g3te4cd6dq_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.IO.FileSystem.Primitives.wasm",
            "name": "System.IO.FileSystem.Primitives.s8z1h6e7bs.wasm",
            "hash": "sha256-rJF209xyy75EKtaVFIXszQG504rNkNMzn5nuQgopOis=",
            "resolvedUrl": System_IO_FileSystem_Primitives_s8z1h6e7bs_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.IO.FileSystem.Watcher.wasm",
            "name": "System.IO.FileSystem.Watcher.jia322rvtg.wasm",
            "hash": "sha256-+1GYGl6rYRxD2mfq9jYmM71NOi5lVQ4ntSd2XgLQr/A=",
            "resolvedUrl": System_IO_FileSystem_Watcher_jia322rvtg_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.IO.FileSystem.wasm",
            "name": "System.IO.FileSystem.y4d3mjm0rs.wasm",
            "hash": "sha256-vjhZDTZZmOeM6kiHpKghpt2WLmy8+s51jgU3nWVoMsQ=",
            "resolvedUrl": System_IO_FileSystem_y4d3mjm0rs_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.IO.IsolatedStorage.wasm",
            "name": "System.IO.IsolatedStorage.vucb3c4eed.wasm",
            "hash": "sha256-12gqkUA+EW9Ok+XjNnztHbwcZdF0sHGfYsGFDhkaJHA=",
            "resolvedUrl": System_IO_IsolatedStorage_vucb3c4eed_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.IO.MemoryMappedFiles.wasm",
            "name": "System.IO.MemoryMappedFiles.slhu4kxyuf.wasm",
            "hash": "sha256-YGcvm9Unqx7Nslxy7M0XGAICX/34Ac4rHJ1GgryesTc=",
            "resolvedUrl": System_IO_MemoryMappedFiles_slhu4kxyuf_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.IO.Pipelines.wasm",
            "name": "System.IO.Pipelines.d9mh1mhl6a.wasm",
            "hash": "sha256-D2zGD+7FWanAQkGdcxPpzhuAqr1TBe3FwbCzyOEeDDc=",
            "resolvedUrl": System_IO_Pipelines_d9mh1mhl6a_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.IO.Pipes.wasm",
            "name": "System.IO.Pipes.6axf1j8g7w.wasm",
            "hash": "sha256-wlyNSMlOoSLtBjH4ahAIRUF/EIraBRKRS1mqdDW1BEc=",
            "resolvedUrl": System_IO_Pipes_6axf1j8g7w_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.IO.Pipes.AccessControl.wasm",
            "name": "System.IO.Pipes.AccessControl.aawhalzgje.wasm",
            "hash": "sha256-sqt6fPu+tFpCJkBRmzTwCeFxWuNkv9Qvw6RRdtyLi/Y=",
            "resolvedUrl": System_IO_Pipes_AccessControl_aawhalzgje_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.IO.UnmanagedMemoryStream.wasm",
            "name": "System.IO.UnmanagedMemoryStream.jtjypz1bw3.wasm",
            "hash": "sha256-141FexMM+rgyJjbPV3Tqjins7IWx6QuAhdpWiGB8+wU=",
            "resolvedUrl": System_IO_UnmanagedMemoryStream_jtjypz1bw3_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.IO.wasm",
            "name": "System.IO.u03kcd51u2.wasm",
            "hash": "sha256-TrxbGqQLHCo+lECXU34H+9JxDj+EGVrjMrSa4JIhTpQ=",
            "resolvedUrl": System_IO_u03kcd51u2_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Linq.AsyncEnumerable.wasm",
            "name": "System.Linq.AsyncEnumerable.qecnowng39.wasm",
            "hash": "sha256-kqYBPZyPoXxShJfn6Z4etPhqhi7wXEdqUL+YvjNrMa0=",
            "resolvedUrl": System_Linq_AsyncEnumerable_qecnowng39_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Linq.Expressions.wasm",
            "name": "System.Linq.Expressions.48ueoty928.wasm",
            "hash": "sha256-hJy4f+HmGU4Xh4kza5OapM/UfFxlTnF0arbzmWUqEPM=",
            "resolvedUrl": System_Linq_Expressions_48ueoty928_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Linq.Parallel.wasm",
            "name": "System.Linq.Parallel.6f9mb35gje.wasm",
            "hash": "sha256-MgAUM4mdUmMKSpa9kowpy8pfI5inqZyMsDmFDZYeKFU=",
            "resolvedUrl": System_Linq_Parallel_6f9mb35gje_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Linq.Queryable.wasm",
            "name": "System.Linq.Queryable.bv9ee87bon.wasm",
            "hash": "sha256-d0dArG+pt8tZXMUGpFhpI18ztIMnH4waXkZ6KsKhqEI=",
            "resolvedUrl": System_Linq_Queryable_bv9ee87bon_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Linq.wasm",
            "name": "System.Linq.ffyao6dtjo.wasm",
            "hash": "sha256-y8xCXRiQfpwTV3uVD+XU9IZyYOBV3IUhbEzI8/FlECQ=",
            "resolvedUrl": System_Linq_ffyao6dtjo_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Memory.wasm",
            "name": "System.Memory.i6uetwkqik.wasm",
            "hash": "sha256-Sg0IsbGxUOdZ+8GEG2rO5+mJutwVXDv/c3DWvC0Y1LM=",
            "resolvedUrl": System_Memory_i6uetwkqik_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Net.Http.wasm",
            "name": "System.Net.Http.9odm4o2yfw.wasm",
            "hash": "sha256-R42oDv3s94KCE4lozGAQ19Wa5RpsPf0UyE2KXiTvME8=",
            "resolvedUrl": System_Net_Http_9odm4o2yfw_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Net.Http.Json.wasm",
            "name": "System.Net.Http.Json.lkg2pjap28.wasm",
            "hash": "sha256-ZYX5QZNYs9FBGbSqFOEHB1CSxj2o3HZlYgB+H8dfls8=",
            "resolvedUrl": System_Net_Http_Json_lkg2pjap28_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Net.HttpListener.wasm",
            "name": "System.Net.HttpListener.l269ngb85c.wasm",
            "hash": "sha256-/e2K/6DbHfR5WYWSWPEyXxwbwdeGUtCBfIxY3PyKhaI=",
            "resolvedUrl": System_Net_HttpListener_l269ngb85c_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Net.Mail.wasm",
            "name": "System.Net.Mail.7s5j7d2u11.wasm",
            "hash": "sha256-B/+5emPbFkpK82pnJSnAmTzRuY7KRKi7wvTfFnULfh4=",
            "resolvedUrl": System_Net_Mail_7s5j7d2u11_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Net.NameResolution.wasm",
            "name": "System.Net.NameResolution.y3a25frvkb.wasm",
            "hash": "sha256-Ui9OtscoCa7Rn+R6uggcz2M7ywzHL64xb5x7hqyrRl8=",
            "resolvedUrl": System_Net_NameResolution_y3a25frvkb_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Net.NetworkInformation.wasm",
            "name": "System.Net.NetworkInformation.6hh4xfj68d.wasm",
            "hash": "sha256-ej7w2CMrRXME9AFCOPRuLwPTCNJ5MfZYGaGYL4xpwiU=",
            "resolvedUrl": System_Net_NetworkInformation_6hh4xfj68d_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Net.Ping.wasm",
            "name": "System.Net.Ping.blplcg5q7j.wasm",
            "hash": "sha256-Me8xKvJFcpGmQz0AsV1c2r9vpfCIAUEvRS8wXBIlB8s=",
            "resolvedUrl": System_Net_Ping_blplcg5q7j_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Net.Primitives.wasm",
            "name": "System.Net.Primitives.52l52f7ge8.wasm",
            "hash": "sha256-I7jNDlc6aY2jmzdqNKjL/MZtDTu9OXjmsPD4nXyeFio=",
            "resolvedUrl": System_Net_Primitives_52l52f7ge8_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Net.Quic.wasm",
            "name": "System.Net.Quic.aw952wuprs.wasm",
            "hash": "sha256-WqNOpml+SO8zGljApjO78PEvcIFKqDWK0LVTSKsFxIA=",
            "resolvedUrl": System_Net_Quic_aw952wuprs_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Net.Requests.wasm",
            "name": "System.Net.Requests.k1mlkdyp0u.wasm",
            "hash": "sha256-KFXJT6HgrkCQDdxsNE6gg+fQHpN4MfAkgt3h6YXYvbM=",
            "resolvedUrl": System_Net_Requests_k1mlkdyp0u_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Net.Security.wasm",
            "name": "System.Net.Security.vm6w2umju9.wasm",
            "hash": "sha256-l4OlxqQwu+sYhjLriuJSuMwHNJ+r/BGVrXp92H+S3NQ=",
            "resolvedUrl": System_Net_Security_vm6w2umju9_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Net.ServerSentEvents.wasm",
            "name": "System.Net.ServerSentEvents.adh3kntjlv.wasm",
            "hash": "sha256-rlq89aqpXUJXWQVg3RSoAJlvCpD0wwttk75XFhW7Bps=",
            "resolvedUrl": System_Net_ServerSentEvents_adh3kntjlv_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Net.ServicePoint.wasm",
            "name": "System.Net.ServicePoint.v8e4bui0ea.wasm",
            "hash": "sha256-4Zv++S2GQBCLLKZxZWQfRW3vgfFi9iddABhfC0/YyoM=",
            "resolvedUrl": System_Net_ServicePoint_v8e4bui0ea_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Net.Sockets.wasm",
            "name": "System.Net.Sockets.ts6iauk88c.wasm",
            "hash": "sha256-E0uMfyK7c1v52Ax4PT7Al8rOKXtImY4piCmVV1h36sA=",
            "resolvedUrl": System_Net_Sockets_ts6iauk88c_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Net.WebClient.wasm",
            "name": "System.Net.WebClient.od0uvbnjit.wasm",
            "hash": "sha256-bBhkM6WRjt9KHqN/yR59aCMpgcg5lxrlHszjVilE9h0=",
            "resolvedUrl": System_Net_WebClient_od0uvbnjit_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Net.WebHeaderCollection.wasm",
            "name": "System.Net.WebHeaderCollection.qf50eeagye.wasm",
            "hash": "sha256-hoFgNcO9TCczLQwT6qI84eb3J3qaSt95k4Jk1T6d4G8=",
            "resolvedUrl": System_Net_WebHeaderCollection_qf50eeagye_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Net.WebProxy.wasm",
            "name": "System.Net.WebProxy.iavyox40gr.wasm",
            "hash": "sha256-6k7n9kRTIjsvj7OlfcaAt7JtbA1BYzx/krXVj9B2al8=",
            "resolvedUrl": System_Net_WebProxy_iavyox40gr_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Net.WebSockets.Client.wasm",
            "name": "System.Net.WebSockets.Client.qeq362432l.wasm",
            "hash": "sha256-jknBfKz+BaTegYBkGof7b0J6y4A2yMaaO9A7SYAq0XY=",
            "resolvedUrl": System_Net_WebSockets_Client_qeq362432l_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Net.WebSockets.wasm",
            "name": "System.Net.WebSockets.oek2p1up7o.wasm",
            "hash": "sha256-MK9DXHHm6AKhF5Je0S8xPCHgY0e0z4i+GMJ6TMhNOoI=",
            "resolvedUrl": System_Net_WebSockets_oek2p1up7o_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Net.wasm",
            "name": "System.Net.i47gzsagt6.wasm",
            "hash": "sha256-7uRJ8dcmw3n7qTbvh1xPYnY0vmPYLapuPLmNF0k4e8I=",
            "resolvedUrl": System_Net_i47gzsagt6_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Numerics.Vectors.wasm",
            "name": "System.Numerics.Vectors.n63j92lpq0.wasm",
            "hash": "sha256-3xe0W+JmwQNNagoBuTG5UEUhgmotX8PmdKsPci5JnPA=",
            "resolvedUrl": System_Numerics_Vectors_n63j92lpq0_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Numerics.wasm",
            "name": "System.Numerics.rkirn4ewf6.wasm",
            "hash": "sha256-y32dbXS60w1HfL97xSaaoig78fCA2hQirSUfe9ZGl30=",
            "resolvedUrl": System_Numerics_rkirn4ewf6_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.ObjectModel.wasm",
            "name": "System.ObjectModel.i378yy84uu.wasm",
            "hash": "sha256-EoCxZODvWTOXgxLrSIGtMa2yjMKLeeK1JlfMuIAfMbc=",
            "resolvedUrl": System_ObjectModel_i378yy84uu_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Private.DataContractSerialization.wasm",
            "name": "System.Private.DataContractSerialization.sny9oej0eg.wasm",
            "hash": "sha256-SKiQcTZvy/3E4yYZXbAuxojifAxqCAALhFducmouT7M=",
            "resolvedUrl": System_Private_DataContractSerialization_sny9oej0eg_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Private.Uri.wasm",
            "name": "System.Private.Uri.esflyl9tnu.wasm",
            "hash": "sha256-LunJHkRu5RgV5fHjx5eDdT17J4Gtytr6/oKJwO1d8R0=",
            "resolvedUrl": System_Private_Uri_esflyl9tnu_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Private.Xml.Linq.wasm",
            "name": "System.Private.Xml.Linq.memmbl9fkq.wasm",
            "hash": "sha256-EjZ9rzHEnY68PXt05q3B/kbsekIDstXc7Sn5XRDvk1E=",
            "resolvedUrl": System_Private_Xml_Linq_memmbl9fkq_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Private.Xml.wasm",
            "name": "System.Private.Xml.zw12n4yipy.wasm",
            "hash": "sha256-Mx0L0SdPw6FcjUMwHIOX5eE8YOVQ6jMFlrmRiK+KkhQ=",
            "resolvedUrl": System_Private_Xml_zw12n4yipy_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Reflection.wasm",
            "name": "System.Reflection.14urzokzzb.wasm",
            "hash": "sha256-z8vs8oc+dKapwTQeJ57oDBrsDsHLYwVv3iVtzebNxZo=",
            "resolvedUrl": System_Reflection_14urzokzzb_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Reflection.DispatchProxy.wasm",
            "name": "System.Reflection.DispatchProxy.3vrt3hlgt3.wasm",
            "hash": "sha256-T0nAzwQ4akMO9XJb2cgIhZsnc78p2+fxtnVD40l1XA4=",
            "resolvedUrl": System_Reflection_DispatchProxy_3vrt3hlgt3_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Reflection.Emit.ILGeneration.wasm",
            "name": "System.Reflection.Emit.ILGeneration.ng7j4cz770.wasm",
            "hash": "sha256-eS8SCSrXQiex2kgp1qxb8IRQqdqTozoZXpt+CoM5v1U=",
            "resolvedUrl": System_Reflection_Emit_ILGeneration_ng7j4cz770_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Reflection.Emit.Lightweight.wasm",
            "name": "System.Reflection.Emit.Lightweight.v2imlilbpe.wasm",
            "hash": "sha256-B9LTebLgF9ZAw2P8ct6U1bZ5++SFfNBBam+PO4a4ELo=",
            "resolvedUrl": System_Reflection_Emit_Lightweight_v2imlilbpe_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Reflection.Emit.wasm",
            "name": "System.Reflection.Emit.udsnuk3ij6.wasm",
            "hash": "sha256-jrKQzTgqMdjuqWHUGsPfnCbqnWpXQkvKfDfBSM+Mt0g=",
            "resolvedUrl": System_Reflection_Emit_udsnuk3ij6_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Reflection.Extensions.wasm",
            "name": "System.Reflection.Extensions.182qm6arqh.wasm",
            "hash": "sha256-P2Qk2OLacQqt2Llp1r47hyo48QLV06MpAlAZXa35u8c=",
            "resolvedUrl": System_Reflection_Extensions_182qm6arqh_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Reflection.Metadata.wasm",
            "name": "System.Reflection.Metadata.5mrm43mmwy.wasm",
            "hash": "sha256-TYUTGCId0GxyLd2Jv9MzjCaGIiyVHaCQ9dFDARceTQA=",
            "resolvedUrl": System_Reflection_Metadata_5mrm43mmwy_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Reflection.Primitives.wasm",
            "name": "System.Reflection.Primitives.2qdwh2ddjb.wasm",
            "hash": "sha256-ehYMFAYAGb49ZR5SbH909AXh3SHOi+60MV9aTsA4eNc=",
            "resolvedUrl": System_Reflection_Primitives_2qdwh2ddjb_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Reflection.TypeExtensions.wasm",
            "name": "System.Reflection.TypeExtensions.ohzfghpj7u.wasm",
            "hash": "sha256-bKXQADwSKixye7MZzqGBpXSYCeCyp5ReO68y4cVZsnE=",
            "resolvedUrl": System_Reflection_TypeExtensions_ohzfghpj7u_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Resources.Reader.wasm",
            "name": "System.Resources.Reader.o5l4jqwkcn.wasm",
            "hash": "sha256-HN9FyXSEtGpwrjaWYqr0BYnAF5LhvVg5YyAOfj59K/w=",
            "resolvedUrl": System_Resources_Reader_o5l4jqwkcn_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Resources.ResourceManager.wasm",
            "name": "System.Resources.ResourceManager.u0kj92um25.wasm",
            "hash": "sha256-4sISLJ4CC3eWntk5TFLGQP39skJD43fZt2tHEKDrQ7w=",
            "resolvedUrl": System_Resources_ResourceManager_u0kj92um25_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Resources.Writer.wasm",
            "name": "System.Resources.Writer.fybhus075b.wasm",
            "hash": "sha256-x+TbpI+502ck5N3QVtiTNFvaRmEzfM6AUQezqym5L34=",
            "resolvedUrl": System_Resources_Writer_fybhus075b_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Runtime.CompilerServices.Unsafe.wasm",
            "name": "System.Runtime.CompilerServices.Unsafe.a2v8uztr6q.wasm",
            "hash": "sha256-vtZgLdKVZ4XAh9LNuAg07pdJWav2ZTSdji3wBl6Tnak=",
            "resolvedUrl": System_Runtime_CompilerServices_Unsafe_a2v8uztr6q_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Runtime.CompilerServices.VisualC.wasm",
            "name": "System.Runtime.CompilerServices.VisualC.h9ibm7eot2.wasm",
            "hash": "sha256-NWO0niQAT2U38QP2p678YrYceWxfcGZiao7o4SRm/lI=",
            "resolvedUrl": System_Runtime_CompilerServices_VisualC_h9ibm7eot2_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Runtime.Extensions.wasm",
            "name": "System.Runtime.Extensions.oueezzziy7.wasm",
            "hash": "sha256-sGE96dH2hSZ2o95KQyX/KhuQGnsAFEHdXfHf2rJrSYY=",
            "resolvedUrl": System_Runtime_Extensions_oueezzziy7_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Runtime.Handles.wasm",
            "name": "System.Runtime.Handles.pegxo55rww.wasm",
            "hash": "sha256-r9JfKdsE92m/EaS+G9TnZ8alLJ37y9loMJDwpuhyt/4=",
            "resolvedUrl": System_Runtime_Handles_pegxo55rww_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Runtime.InteropServices.wasm",
            "name": "System.Runtime.InteropServices.76dk94nxht.wasm",
            "hash": "sha256-r2gFCA+pgFEb2L9UVD2ikpc1TLVxMjWdkQeuSoAnsdk=",
            "resolvedUrl": System_Runtime_InteropServices_76dk94nxht_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Runtime.InteropServices.RuntimeInformation.wasm",
            "name": "System.Runtime.InteropServices.RuntimeInformation.himsthc3a6.wasm",
            "hash": "sha256-+QFSX4Pa6oEemBGAbLSx6ddeliBDgepZ+wpUyQqPbsw=",
            "resolvedUrl": System_Runtime_InteropServices_RuntimeInformation_himsthc3a6_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Runtime.Intrinsics.wasm",
            "name": "System.Runtime.Intrinsics.pxgyd8cnpl.wasm",
            "hash": "sha256-w6hLmBdElU3LjlQ9rQqwli8s0kgF20g9OJ+9AbS+AZw=",
            "resolvedUrl": System_Runtime_Intrinsics_pxgyd8cnpl_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Runtime.Loader.wasm",
            "name": "System.Runtime.Loader.smrevcdjv4.wasm",
            "hash": "sha256-nL05yWh/sEH+UGtqYaYsoxnQbkeZ2wfZIJdSlMdUVuU=",
            "resolvedUrl": System_Runtime_Loader_smrevcdjv4_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Runtime.Numerics.wasm",
            "name": "System.Runtime.Numerics.c79r80p0xa.wasm",
            "hash": "sha256-qAA+g2R9BL+KkiVw/uSdgBzcMyh6lHjvE8btAPFlf44=",
            "resolvedUrl": System_Runtime_Numerics_c79r80p0xa_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Runtime.Serialization.Formatters.wasm",
            "name": "System.Runtime.Serialization.Formatters.5nvzlh22y8.wasm",
            "hash": "sha256-8UOlvjQNeuJCJVs/80yedxgANKXEv+u783ZjeVX9lxA=",
            "resolvedUrl": System_Runtime_Serialization_Formatters_5nvzlh22y8_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Runtime.Serialization.Json.wasm",
            "name": "System.Runtime.Serialization.Json.4u1byp8ddu.wasm",
            "hash": "sha256-9PvflPMpTgXt/CJr9aPaMFxrcfb9dfyCWfQcHjBZ/Lw=",
            "resolvedUrl": System_Runtime_Serialization_Json_4u1byp8ddu_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Runtime.Serialization.Primitives.wasm",
            "name": "System.Runtime.Serialization.Primitives.0677mizgg8.wasm",
            "hash": "sha256-CK47ivfrIuMw7yupuqFV24kdTeMPxzka4edYB/BUK9I=",
            "resolvedUrl": System_Runtime_Serialization_Primitives_0677mizgg8_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Runtime.Serialization.Xml.wasm",
            "name": "System.Runtime.Serialization.Xml.7t5vv8ymb3.wasm",
            "hash": "sha256-1ZF5POZglEr7ftw2T6suEvJDtXBOaTWjwiFNqHM6A14=",
            "resolvedUrl": System_Runtime_Serialization_Xml_7t5vv8ymb3_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Runtime.Serialization.wasm",
            "name": "System.Runtime.Serialization.xyb15dfsu9.wasm",
            "hash": "sha256-2VvOlAvLHV9Hmwglfi2ZMv/OBChHICovaG0z7JoV4/s=",
            "resolvedUrl": System_Runtime_Serialization_xyb15dfsu9_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Runtime.wasm",
            "name": "System.Runtime.p5wq7tenlq.wasm",
            "hash": "sha256-sz8dIFsswef1k1Xw7c0y0c+hKmO4ytyw9WoY17i+cAU=",
            "resolvedUrl": System_Runtime_p5wq7tenlq_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Security.AccessControl.wasm",
            "name": "System.Security.AccessControl.9gsd8k6c5m.wasm",
            "hash": "sha256-t/Zhd+N29fKKAil705KO0KB8ytqtRMQ2gIUgle8vQxU=",
            "resolvedUrl": System_Security_AccessControl_9gsd8k6c5m_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Security.Claims.wasm",
            "name": "System.Security.Claims.l0ljqfaxwo.wasm",
            "hash": "sha256-JcfUg2gv1aAcLpXhGHpawjy/Te3Qg9fcheHexSFJsec=",
            "resolvedUrl": System_Security_Claims_l0ljqfaxwo_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Security.Cryptography.wasm",
            "name": "System.Security.Cryptography.2ufhqnonzh.wasm",
            "hash": "sha256-asDjhVEPO/QN5eJuPJXMjymopyT1voLEIPbRMo6SpJM=",
            "resolvedUrl": System_Security_Cryptography_2ufhqnonzh_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Security.Cryptography.Algorithms.wasm",
            "name": "System.Security.Cryptography.Algorithms.i0xv6lwo7z.wasm",
            "hash": "sha256-4vLqnvj48uhFTeNavhXSPLej8+a04vT4Q4BzeA7aOJc=",
            "resolvedUrl": System_Security_Cryptography_Algorithms_i0xv6lwo7z_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Security.Cryptography.Cng.wasm",
            "name": "System.Security.Cryptography.Cng.eeehrv0scy.wasm",
            "hash": "sha256-2sUCiaSAJyXHG/+fD4xucBGmWPzXRTVKmMn/tpyrwMs=",
            "resolvedUrl": System_Security_Cryptography_Cng_eeehrv0scy_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Security.Cryptography.Csp.wasm",
            "name": "System.Security.Cryptography.Csp.2c9gox8ebd.wasm",
            "hash": "sha256-vjBPq4f54O+P5zvokIBJUwXcSpez9Y+PGc0wtzqW6W4=",
            "resolvedUrl": System_Security_Cryptography_Csp_2c9gox8ebd_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Security.Cryptography.Encoding.wasm",
            "name": "System.Security.Cryptography.Encoding.axny8s8hqs.wasm",
            "hash": "sha256-Ypo7uNzxzTGJIC9jT/m/b4kN4txarBQHeb5H9RXu54k=",
            "resolvedUrl": System_Security_Cryptography_Encoding_axny8s8hqs_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Security.Cryptography.OpenSsl.wasm",
            "name": "System.Security.Cryptography.OpenSsl.1sxm4ayhhy.wasm",
            "hash": "sha256-gaDsyTJrtp4XzQYUXgmfB6RLIGi2FjWj8Ar3P9WDT/4=",
            "resolvedUrl": System_Security_Cryptography_OpenSsl_1sxm4ayhhy_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Security.Cryptography.Primitives.wasm",
            "name": "System.Security.Cryptography.Primitives.4ri0a9h1v4.wasm",
            "hash": "sha256-8LydFSbE7V5LdD50fXdpmeIt/m2sksGJqzMY5e6vP4Y=",
            "resolvedUrl": System_Security_Cryptography_Primitives_4ri0a9h1v4_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Security.Cryptography.X509Certificates.wasm",
            "name": "System.Security.Cryptography.X509Certificates.rncai5fzz3.wasm",
            "hash": "sha256-aWvCLknwVe+mKmgXO2xDc5IhC7B6PLDmWOFFf933b/o=",
            "resolvedUrl": System_Security_Cryptography_X509Certificates_rncai5fzz3_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Security.Principal.wasm",
            "name": "System.Security.Principal.1tjw5xr7f1.wasm",
            "hash": "sha256-ReXTuJnKsfUr67WZ7ekLkKwdCADP7MLsv5+8Rz9kUbM=",
            "resolvedUrl": System_Security_Principal_1tjw5xr7f1_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Security.Principal.Windows.wasm",
            "name": "System.Security.Principal.Windows.9gaajfy1mm.wasm",
            "hash": "sha256-aUJqbqjVpUgPNm/I10ZI5tTZvdAHZuXZHM1+bgtEh2Q=",
            "resolvedUrl": System_Security_Principal_Windows_9gaajfy1mm_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Security.SecureString.wasm",
            "name": "System.Security.SecureString.hbgjudeeg7.wasm",
            "hash": "sha256-XWsfTgbMvq8bIErWG9SRS12GjUA70gfii8Zn1x06wo4=",
            "resolvedUrl": System_Security_SecureString_hbgjudeeg7_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Security.wasm",
            "name": "System.Security.ua0hvadql2.wasm",
            "hash": "sha256-xlJkQOl9i3U3R0rHBnv42WUyxpyTfb4NWeQP9thvn8k=",
            "resolvedUrl": System_Security_ua0hvadql2_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.ServiceModel.Web.wasm",
            "name": "System.ServiceModel.Web.a5nn12k2du.wasm",
            "hash": "sha256-EoKhlu6LFf/rxXLNN0AG/ox3cVZSQZcoJHRMn3Ai3ZM=",
            "resolvedUrl": System_ServiceModel_Web_a5nn12k2du_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.ServiceProcess.wasm",
            "name": "System.ServiceProcess.mzhiaa0o9m.wasm",
            "hash": "sha256-kt3OXJkHVIZ0HaKa/O5mKV/gKK2VRUPUcZlR1mMEWqI=",
            "resolvedUrl": System_ServiceProcess_mzhiaa0o9m_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Text.Encoding.CodePages.wasm",
            "name": "System.Text.Encoding.CodePages.fso8gniga6.wasm",
            "hash": "sha256-fxt6zzGzOdxcGfVxV+shFrqbQSKxSfGJNB7/GqvLsB8=",
            "resolvedUrl": System_Text_Encoding_CodePages_fso8gniga6_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Text.Encoding.Extensions.wasm",
            "name": "System.Text.Encoding.Extensions.jth4fv0ujc.wasm",
            "hash": "sha256-N/7bmYLlo54Cs+Dw8ee7CkFrLzqTe8izc5vm6dvzizM=",
            "resolvedUrl": System_Text_Encoding_Extensions_jth4fv0ujc_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Text.Encoding.wasm",
            "name": "System.Text.Encoding.hen1lijr2s.wasm",
            "hash": "sha256-RybkW+87HvDCrKwpsSLXM3w5TB5EMz7dpLJz1o+fFaI=",
            "resolvedUrl": System_Text_Encoding_hen1lijr2s_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Text.Encodings.Web.wasm",
            "name": "System.Text.Encodings.Web.s63xwzfs6e.wasm",
            "hash": "sha256-ZAoPykd2oHgSQyIJbM0ktKOTvV6mh7f8h4Ev8xRgPJk=",
            "resolvedUrl": System_Text_Encodings_Web_s63xwzfs6e_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Text.Json.wasm",
            "name": "System.Text.Json.e68ojdl57q.wasm",
            "hash": "sha256-ZhbF4R3Uf6o3EptGsHICrb2qcZ2bd1UEGfHtMc+VkxY=",
            "resolvedUrl": System_Text_Json_e68ojdl57q_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Text.RegularExpressions.wasm",
            "name": "System.Text.RegularExpressions.o5g45g5kfe.wasm",
            "hash": "sha256-zN+88osSOmhdIdVgihibx/x1XedKWsCYEgvtcYvxypY=",
            "resolvedUrl": System_Text_RegularExpressions_o5g45g5kfe_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Threading.AccessControl.wasm",
            "name": "System.Threading.AccessControl.vdpdtl3x6u.wasm",
            "hash": "sha256-w+LhmzUpdAZw6jVH9FdmsnN/n9z3LtLHTZR6pyxGSzg=",
            "resolvedUrl": System_Threading_AccessControl_vdpdtl3x6u_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Threading.Channels.wasm",
            "name": "System.Threading.Channels.wty3mw4oue.wasm",
            "hash": "sha256-7JYMWtx5aaztvI6qsJStob8sRMLyMrT6oDNkD24Inaw=",
            "resolvedUrl": System_Threading_Channels_wty3mw4oue_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Threading.Overlapped.wasm",
            "name": "System.Threading.Overlapped.03j0f78xbg.wasm",
            "hash": "sha256-nAsVVeqQDNVR0DaIhT//1tZcxyCxt6LUsnXjhz9mevo=",
            "resolvedUrl": System_Threading_Overlapped_03j0f78xbg_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Threading.Tasks.Dataflow.wasm",
            "name": "System.Threading.Tasks.Dataflow.1qphddued4.wasm",
            "hash": "sha256-h4ITo++VVySJB9UXW1AizkqXGbRc7hysEJPM8paCQBQ=",
            "resolvedUrl": System_Threading_Tasks_Dataflow_1qphddued4_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Threading.Tasks.Extensions.wasm",
            "name": "System.Threading.Tasks.Extensions.a6m6fd88nw.wasm",
            "hash": "sha256-Pgd/40O6xPGNbTDdtZ7IXhjog5u+uiSnSeblax0Kd2w=",
            "resolvedUrl": System_Threading_Tasks_Extensions_a6m6fd88nw_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Threading.Tasks.Parallel.wasm",
            "name": "System.Threading.Tasks.Parallel.hwicu9gnb7.wasm",
            "hash": "sha256-sUxZoPUOtigPLyVhliXgQ79G8wxA8VXhlK86oWh8RhU=",
            "resolvedUrl": System_Threading_Tasks_Parallel_hwicu9gnb7_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Threading.Tasks.wasm",
            "name": "System.Threading.Tasks.ko7yntasn6.wasm",
            "hash": "sha256-ZAZ/8IqOgKMUtbsFNyZ/nGPxSrgnh8t4h5Z+SVjecyY=",
            "resolvedUrl": System_Threading_Tasks_ko7yntasn6_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Threading.Thread.wasm",
            "name": "System.Threading.Thread.ydz2qayyqe.wasm",
            "hash": "sha256-poEtzjS5NNRnotBgaKA2Su+mjp8Y0eXTf7yoYasFvAQ=",
            "resolvedUrl": System_Threading_Thread_ydz2qayyqe_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Threading.ThreadPool.wasm",
            "name": "System.Threading.ThreadPool.4f7y4725hj.wasm",
            "hash": "sha256-ELorBXMa++9uTr6P3crPiAaJMWcEhNok01GvE1QPjjs=",
            "resolvedUrl": System_Threading_ThreadPool_4f7y4725hj_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Threading.Timer.wasm",
            "name": "System.Threading.Timer.23ma43391f.wasm",
            "hash": "sha256-spGH+vGozMmLNWcgCowJHX/L9fBEdoHOEveQK9OdmxI=",
            "resolvedUrl": System_Threading_Timer_23ma43391f_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Threading.wasm",
            "name": "System.Threading.xg0tuqpte9.wasm",
            "hash": "sha256-XwK+Pb+6p1iYXhc2Ks+FjDwPBgIInT2jAWOQ1jEQsNM=",
            "resolvedUrl": System_Threading_xg0tuqpte9_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Transactions.wasm",
            "name": "System.Transactions.1k679dq1nh.wasm",
            "hash": "sha256-DwZ5kQKS8S//BhRIzWNpCBtH7PWn1RUGmLKfunzItyI=",
            "resolvedUrl": System_Transactions_1k679dq1nh_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Transactions.Local.wasm",
            "name": "System.Transactions.Local.vhgy89wfp9.wasm",
            "hash": "sha256-/etlP37Z3fbLkp3G+SuI8t2EuBlW6ArNkc821ii6YrU=",
            "resolvedUrl": System_Transactions_Local_vhgy89wfp9_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.ValueTuple.wasm",
            "name": "System.ValueTuple.gm53bht7l7.wasm",
            "hash": "sha256-yGHllPKjSV7O8dufUyuOGmky/GbwzH0IlPDpgsfuZG4=",
            "resolvedUrl": System_ValueTuple_gm53bht7l7_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Web.HttpUtility.wasm",
            "name": "System.Web.HttpUtility.xg7fizhwzt.wasm",
            "hash": "sha256-b150ZISvFUH8voYwQdhIH/m8vkHcJFY+LFDfuemZ2sw=",
            "resolvedUrl": System_Web_HttpUtility_xg7fizhwzt_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Web.wasm",
            "name": "System.Web.sakoricpi1.wasm",
            "hash": "sha256-PADclgEUTNPtXvaKE9YgE1DRcW/Q+gPLdwMUsbu8esA=",
            "resolvedUrl": System_Web_sakoricpi1_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Windows.wasm",
            "name": "System.Windows.98z5iu2ekq.wasm",
            "hash": "sha256-mWvSjccrBX09Q7oIJzRGJIi6zQl0pJMKkSrWN9kWf6Y=",
            "resolvedUrl": System_Windows_98z5iu2ekq_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Xml.Linq.wasm",
            "name": "System.Xml.Linq.4w1i83kr6d.wasm",
            "hash": "sha256-FM7him4hRwYj3b/rUlLVoDuzrDlojL6J1pMTiiv+prk=",
            "resolvedUrl": System_Xml_Linq_4w1i83kr6d_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Xml.ReaderWriter.wasm",
            "name": "System.Xml.ReaderWriter.gt2wkfrpd2.wasm",
            "hash": "sha256-RPbWKMyF0PcAToP99SZP3iI1LbK+2mMn0hPGIrJuwMI=",
            "resolvedUrl": System_Xml_ReaderWriter_gt2wkfrpd2_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Xml.Serialization.wasm",
            "name": "System.Xml.Serialization.ymsdqvtcj5.wasm",
            "hash": "sha256-OtgEHFA+kC4Og+9sy9AItDeN0G36r5KKP80acvekmKQ=",
            "resolvedUrl": System_Xml_Serialization_ymsdqvtcj5_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Xml.XDocument.wasm",
            "name": "System.Xml.XDocument.nthxjrhml8.wasm",
            "hash": "sha256-hVjT4ogBaijnAej8Jkvvfi/3R1Sz65EOhwcQavaAH8I=",
            "resolvedUrl": System_Xml_XDocument_nthxjrhml8_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Xml.XPath.XDocument.wasm",
            "name": "System.Xml.XPath.XDocument.64udcjx5rc.wasm",
            "hash": "sha256-Sh41QbA3ENHi4CfpbioGcvLy8HrcPA2WVz/gL9fH5lA=",
            "resolvedUrl": System_Xml_XPath_XDocument_64udcjx5rc_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Xml.XPath.wasm",
            "name": "System.Xml.XPath.m6ukudree9.wasm",
            "hash": "sha256-zjcKkw6BNuo0vSx0rlxPlbNuoXjUPWYT9Q1xX548wAU=",
            "resolvedUrl": System_Xml_XPath_m6ukudree9_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Xml.XmlDocument.wasm",
            "name": "System.Xml.XmlDocument.uqap183j3j.wasm",
            "hash": "sha256-WkzkDq1p5Hr8baeIcyUvZkqCz0Qtl39PQvxQVm0EQkA=",
            "resolvedUrl": System_Xml_XmlDocument_uqap183j3j_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Xml.XmlSerializer.wasm",
            "name": "System.Xml.XmlSerializer.kyxgnik2yp.wasm",
            "hash": "sha256-7M5Yf1qrKhJZ1ts2o6P73IDZwOinWT1m/rX9xwhvX18=",
            "resolvedUrl": System_Xml_XmlSerializer_kyxgnik2yp_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.Xml.wasm",
            "name": "System.Xml.gccv71ojjn.wasm",
            "hash": "sha256-QJsL5vasxIwvmlNTlOtLvB4u9y1eFnTzQk9qyIkunDo=",
            "resolvedUrl": System_Xml_gccv71ojjn_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "System.wasm",
            "name": "System.q2zv1ajo7g.wasm",
            "hash": "sha256-UvzF6QRtJEhekaFpn7/fdSo6I77TIVOQYu+B0IvKLAw=",
            "resolvedUrl": System_q2zv1ajo7g_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "WindowsBase.wasm",
            "name": "WindowsBase.nd3r6urbkm.wasm",
            "hash": "sha256-JUj0SUenc5aNsprOIJomnkeHQU0zOM9KnaF9DBYp5as=",
            "resolvedUrl": WindowsBase_nd3r6urbkm_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "mscorlib.wasm",
            "name": "mscorlib.l31p083pc1.wasm",
            "hash": "sha256-L55XGcSlCWjg/nPH8pqX4FoLUqcw89+V7xgu0GWwKSU=",
            "resolvedUrl": mscorlib_l31p083pc1_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "netstandard.wasm",
            "name": "netstandard.8ql06rx5ae.wasm",
            "hash": "sha256-ANBTDvID1f1UUcJOBmDfE5tFVLna1UUVAxMEbtfGiRM=",
            "resolvedUrl": netstandard_8ql06rx5ae_wasm,
            "cache": "force-cache"
          },
          {
            "virtualPath": "websocket-sharp.wasm",
            "name": "websocket-sharp.hxolgqcoj5.wasm",
            "hash": "sha256-ixbQzKVBepGWvsSiiv1XPMGu9/6Qiz03/lcIwo3YJt4=",
            "resolvedUrl": websocket_sharp_hxolgqcoj5_wasm,
            "cache": "force-cache"
          }
        ],
        "libraryInitializers": [
          {
            "name": "SpawnDev.SpawnJS.9pjx9wsgr4.lib.module.js",
            "moduleExports": ___2F_SpawnDev_SpawnJS_9pjx9wsgr4_lib_module_js
          }
        ],
        "modulesAfterRuntimeReady": [
          {
            "name": "../SpawnDev.SpawnJS.9pjx9wsgr4.lib.module.js",
            "moduleExports": ___2F_SpawnDev_SpawnJS_9pjx9wsgr4_lib_module_js
          }
        ]
      },
      "debugLevel": 0,
      "globalizationMode": "sharded",
      "extensions": {
        "blazor": {}
      },
      "runtimeConfig": {
        "runtimeOptions": {
          "configProperties": {
            "System.Diagnostics.Debugger.IsSupported": false,
            "System.Diagnostics.Metrics.Meter.IsSupported": false,
            "System.Diagnostics.Tracing.EventSource.IsSupported": false,
            "System.GC.Server": true,
            "System.Globalization.Invariant": false,
            "System.TimeZoneInfo.Invariant": false,
            "System.Linq.Enumerable.IsSizeOptimized": true,
            "System.Net.Http.EnableActivityPropagation": false,
            "System.Net.Http.WasmEnableStreamingResponse": true,
            "System.Net.SocketsHttpHandler.Http3Support": false,
            "System.Reflection.Metadata.MetadataUpdater.IsSupported": false,
            "System.Resources.UseSystemResourceKeys": true,
            "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization": false,
            "System.Text.Encoding.EnableUnsafeUTF7Encoding": false,
            "System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault": true
          }
        }
      }
    }/*json-end*/);

    // SpawnDev.SpawnJS.WebWorkers - classic/module bundle loader (Rollup ENTRY)
    // -----------------------------------------------------------------------------
    // The MSBuild bundle task copies this file into the bundler-friendly publish's
    // wwwroot (next to spawndev.spawnjs.webworkers.event-holder.js and _framework/)
    // and Rollup bundles it into main.module.js (es) + main.classic.js (umd).
    //
    // The event-holder import is FIRST (side-effect) and load-bearing: it registers
    // SharedWorker/ServiceWorker event listeners at top-level sync eval so early
    // events (onconnect, install, fetch, ...) are captured and held while the async
    // .Net runtime boots below. It must evaluate before the dotnet module graph.

    // Asset URLs (assemblies, dotnet.native.wasm, ICU .dat) are supplied by the bundle's
    // frameworkAssetsPlugin as `new URL('_framework/<name>', import.meta.url)` - i.e. resolved
    // against THIS bundle's own location, pointing at the existing _framework output that ships
    // beside it. Rollup rewrites import.meta.url per format (native in the es bundle; a
    // document.currentScript / self.location shim in the classic bundle), so it self-resolves:
    //   - <script src=".../main.classic.js">  -> .../_framework/<name>
    //   - new Worker(".../main.classic.js")    -> the worker script folder's _framework/<name>
    // No withResourceLoader re-rooting is needed: the bundle reuses the real _framework assets
    // as-is (nothing is emitted or renamed), so the default URIs are already correct.
    async function boot() {
        const runtime = await ft
            .withApplicationArguments('start')
            .create();

        // Dispatch the managed entry point (Program.cs). It runs until Exit(), so
        // runMain() may never resolve - do NOT await it for readiness, just surface a
        // startup error if one is thrown.
        Promise.resolve().then(() => runtime.runMain()).catch(err => console.error('SpawnJS runMain error:', err));
        return runtime;
    }

    // Auto-boot on include so a plain <script> / importScripts / import "just works".
    // Nothing is exposed on globalThis by the loader: each bundle is its own closure,
    // so multiple SpawnJS apps on one page/worker stay isolated. (The event-holder
    // intentionally uses globalThis in ServiceWorker/SharedWorker scopes only, where a
    // scope is single-app by nature - that is the fixed .Net drain contract.)
    //
    // No ad-hoc ServiceWorker install/waitUntil keep-alive here: in a ServiceWorker the
    // event-holder captured `install` at top-level sync eval and holds it via
    // e.waitUntil(promise), keeping the SW alive through the entire async boot until the
    // .Net side's ServiceWorkerEventHandler drains and resolves it.
    boot();

})();
