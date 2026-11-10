import {expect,it} from 'vitest';
import {shortcutKey,shortcutLabel} from './shortcuts';
const event=(key:string,extra={})=>({key,ctrlKey:true,altKey:true,shiftKey:false,metaKey:false,...extra});
it('round trips Ctrl Alt M and Saved through JSON without changing modifier bits',()=>{
 for(const [key,expected] of [['m',393293],['r',393298]] as const){const value=shortcutKey(event(key));expect(value).toBe(expected);expect(shortcutLabel(JSON.parse(JSON.stringify({key:value})).key)).toBe('Ctrl + Alt + '+key.toUpperCase());}
});
it('captures only keys and modifiers accepted by native policy',()=>{
 for(const key of ['Control','Alt','Shift','Meta','Tab','Escape','F12','F24',';'])expect(shortcutKey(event(key))).toBeUndefined();
 expect(shortcutKey(event('m',{metaKey:true}))).toBeUndefined();expect(shortcutKey(event('m',{ctrlKey:false,altKey:false}))).toBeUndefined();
 expect(shortcutKey({...event('!',{shiftKey:true}),code:'Digit1'})).toBe(0x70031);
 expect(shortcutKey(event('F11',{shiftKey:true}))).toBe(0x7007a);
});
