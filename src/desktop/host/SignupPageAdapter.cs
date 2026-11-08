using System;
using System.Text.Json;

namespace RobloxMultiInstance.Desktop;

internal static class SignupPageAdapter
{
    // Runs only in the top-level document, checks its origin again, and never submits.
    internal static string Script(string username,string password,DateOnly birthdate,bool fill)=>
        "(()=>{const input="+JsonSerializer.Serialize(new {username,password,month=birthdate.Month,day=birthdate.Day,year=birthdate.Year,fill})+";"+Body+"})()";
    internal const string Body="""
        if(window!==window.top || location.protocol!=='https:' || !['www.roblox.com','roblox.com'].includes(location.hostname) || location.port && location.port!=='443')return {state:'Blocked'};
        const visible=e=>!!e && e.getClientRects().length>0 && getComputedStyle(e).visibility!=='hidden';
        const challenge=[...document.querySelectorAll('iframe')].some(e=>visible(e)&&/arkose|funcaptcha|captcha|challenge/i.test(e.src+' '+e.title));
        if(challenge)return {state:'Challenge'};
        const errors=[...document.querySelectorAll('[role="alert"],.form-control-label.text-error,[id$="InputValidation"]')].filter(visible);
        const rejected=errors.some(e=>/username.*(already|taken|use|available)|already.*(username|use)/i.test(e.textContent||''));
        if(rejected&&!input.fill)return {state:'UsernameRejected'};
        const u=document.querySelector('#signup-username'),p=document.querySelector('#signup-password, #signup-v2-password');
        const birthday=id=>{const root=document.querySelector(id);return root?.tagName==='SELECT'?root:root?.querySelector('select');};
        const m=birthday('#MonthDropdown'),d=birthday('#DayDropdown'),y=birthday('#YearDropdown');
        const shown=e=>visible(e)||visible(e?.closest('[data-testid^="birthday-"]'));
        const passwordShown=e=>visible(e)||(e?.id==='signup-v2-password'&&visible(e.closest('[data-testid="text-input-container"]')));
        if(!visible(u)||!p||(!input.fill&&!passwordShown(p))||![m,d,y].every(shown)||u.tagName!=='INPUT'||p.tagName!=='INPUT'||p.type!=='password'||![m,d,y].every(e=>e.tagName==='SELECT'))return {state:'Unrecognized'};
        if(input.fill)return (async()=>{
            const set=(e,v)=>{Object.getOwnPropertyDescriptor(e.tagName==='SELECT'?HTMLSelectElement.prototype:HTMLInputElement.prototype,'value').set.call(e,v);e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));};
            const months=['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
            const select=async(id,n,month=false)=>{const e=birthday(id);const o=[...e.options].find(o=>o.value!==''&&(Number(o.value)===n||month&&o.value===months[n-1]));if(!o)throw Error('Unsupported signup form');set(e,o.value);await new Promise(resolve=>setTimeout(resolve,60));return o.value;};
            try{
                const mv=await select('#MonthDropdown',input.month,true);
                const dv=await select('#DayDropdown',input.day);
                const yv=await select('#YearDropdown',input.year);
                set(u,input.username);
                u.dispatchEvent(new Event("blur",{bubbles:true}));
                await new Promise(resolve=>setTimeout(resolve,300));
                const password=document.querySelector('#signup-password, #signup-v2-password');
                if(!passwordShown(password))return {state:'Partial'};
                set(password,input.password);
                await new Promise(resolve=>setTimeout(resolve,60));
                if(birthday('#MonthDropdown').value!==mv||birthday('#DayDropdown').value!==dv||birthday('#YearDropdown').value!==yv||u.value!==input.username||password.value!==input.password)return {state:'Partial'};
            }catch{return {state:'Partial'};}
            return {state:'Filled'};
        })();
        if(errors.some(e=>(e.textContent||'').trim()))return {state:'Rejected'};
        return {state:'Ready'};
        """;
}
