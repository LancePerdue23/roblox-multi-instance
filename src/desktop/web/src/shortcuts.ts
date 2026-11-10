export function shortcutKey(event:{key:string;code?:string;ctrlKey:boolean;altKey:boolean;shiftKey:boolean;metaKey:boolean}):number|undefined {
 if(event.metaKey)return;
 const physical=/^(Key[A-Z]|Digit[0-9])$/.test(event.code??'')?event.code!.slice(-1):event.key;
 const code=/^[a-z0-9]$/i.test(physical)?physical.toUpperCase().charCodeAt(0):/^F([1-9]|10|11)$/.test(event.key)?111+Number(event.key.slice(1)):0;
 if(code&&(event.ctrlKey||event.altKey||event.shiftKey))return code|(event.ctrlKey?131072:0)|(event.altKey?262144:0)|(event.shiftKey?65536:0);
}
export function shortcutLabel(key:number){return [(key&131072)?'Ctrl':'',(key&262144)?'Alt':'',(key&65536)?'Shift':'',(key&65535)>=112&&(key&65535)<=122?'F'+((key&65535)-111):String.fromCharCode(key&65535)].filter(Boolean).join(' + ')}
