import {test,expect} from '@playwright/test';
import {readFileSync} from 'node:fs';
import path from 'node:path';

// Exercise the exact script shipped by the native host against local HTML only.
const source=readFileSync(path.join(process.cwd(),'../host/SignupPageAdapter.cs'),'utf8');
const body=source.match(/internal const string Body="""([\s\S]*?)""";/)![1];
const form=`<form onsubmit="window.submitted=true;return false"><input id="signup-username"><input id="signup-password" type="password"><select id="MonthDropdown"><option value="">Month</option><option value="02">February</option></select><select id="DayDropdown"><option value="">Day</option><option value="03">3</option></select><select id="YearDropdown"><option value="">Year</option><option value="1990">1990</option></select><button>Signup</button></form>`;
const script=(fill=true,username='Fixture001')=>`(()=>{const input=${JSON.stringify({username,password:'FixturePassword123',month:2,day:3,year:1990,fill})};${body}})()`;

test.beforeEach(async({page})=>{
 await page.route('**/*',route=>route.request().isNavigationRequest()&&route.request().frame()===page.mainFrame()?route.fulfill({body:form,contentType:'text/html'}):route.abort());
 await page.goto('https://www.roblox.com/');
});
test('autofill dispatches events and leaves signup submission to the user',async({page})=>{
 await page.evaluate(()=>{(window as any).changes=0;document.addEventListener('change',()=>{(window as any).changes++;});});
 expect(await page.evaluate(script())).toEqual({state:'Filled'});
 await expect(page.locator('#signup-username')).toHaveValue('Fixture001');await expect(page.locator('#signup-password')).toHaveValue('FixturePassword123');
 await expect(page.locator('#MonthDropdown')).toHaveValue('02');await expect(page.locator('#DayDropdown')).toHaveValue('03');
 expect(await page.evaluate(()=>(window as any).changes)).toBe(5);expect(await page.evaluate(()=>(window as any).submitted)).toBeUndefined();
 await page.getByRole('button',{name:'Signup'}).click();expect(await page.evaluate(()=>(window as any).submitted)).toBe(true);
});
test('monitoring preserves edits and explicit refill can replace a rejected name',async({page})=>{
 await page.evaluate(script());await page.locator('#signup-username').fill('ManualEdit');await page.evaluate(script(false));await expect(page.locator('#signup-username')).toHaveValue('ManualEdit');
 await page.evaluate(()=>{const error=document.createElement('div');error.id='signup-usernameInputValidation';error.textContent='Username is already in use';document.body.append(error);});
 expect(await page.evaluate(script(false))).toEqual({state:'UsernameRejected'});
 expect(await page.evaluate(script(true,'Fixture002'))).toEqual({state:'Filled'});await expect(page.locator('#signup-username')).toHaveValue('Fixture002');
});
test('challenge detection waits without touching the challenge frame or filling credentials',async({page})=>{
 await page.evaluate(()=>{const frame=document.createElement('iframe');frame.title='captcha challenge';document.body.append(frame);});
 expect(await page.evaluate(script())).toEqual({state:'Challenge'});await expect(page.locator('#signup-password')).toHaveValue('');
});
test('delayed or changed signup forms fail closed and can be retried',async({page})=>{
 await page.setContent('<p>Loading</p>');expect(await page.evaluate(script())).toEqual({state:'Unrecognized'});
 await page.setContent(form);expect(await page.evaluate(script())).toEqual({state:'Filled'});
 await page.locator('#signup-password').evaluate(e=>e.id='changed-password');expect(await page.evaluate(script())).toEqual({state:'Unrecognized'});
});
test('unexpected origins and child frames refuse autofill',async({page})=>{
 await page.goto('https://example.invalid/');expect(await page.evaluate(script())).toEqual({state:'Blocked'});
 await page.goto('https://www.roblox.com/');await page.evaluate(()=>{const frame=document.createElement('iframe');frame.srcdoc='<input id="signup-password">';document.body.append(frame);});
 await expect.poll(()=>page.frames().length).toBe(2);expect(await page.frames()[1].evaluate(script())).toEqual({state:'Blocked'});
});

test('modern signup supports wrapped selects, named months and the v2 password widget',async({page})=>{
 await page.evaluate(()=>{
  for(const id of ['MonthDropdown','DayDropdown','YearDropdown']){
   const select=document.getElementById(id)! as HTMLSelectElement;
   const wrapper=document.createElement('div');wrapper.id=id;wrapper.dataset.testid='birthday-'+id;
   select.removeAttribute('id');select.before(wrapper);wrapper.append(select);
   select.style.visibility='hidden';
   if(id==='MonthDropdown')select.options[1].value='Feb';
  }
  const password=document.getElementById('signup-password')!;
  const wrapper=document.createElement('div');wrapper.dataset.testid='text-input-container';wrapper.style.height='40px';
  password.before(wrapper);wrapper.append(password);password.id='signup-v2-password';password.style.visibility='hidden';
 });
 expect(await page.evaluate(script())).toEqual({state:'Filled'});
 await expect(page.locator('#signup-v2-password')).toHaveValue('FixturePassword123');
 await expect(page.locator('#MonthDropdown select')).toHaveValue('Feb');
 expect(await page.evaluate(()=>(window as any).submitted)).toBeUndefined();
});

test('partial autofill reports a manual recovery state and monitoring preserves corrections',async({page})=>{
 await page.locator('#DayDropdown option[value="03"]').evaluate(e=>e.remove());
 expect(await page.evaluate(script())).toEqual({state:'Partial'});
 await page.locator('#signup-username').fill('ManualCorrection');
 expect(await page.evaluate(script(false))).toEqual({state:'Ready'});
 await expect(page.locator('#signup-username')).toHaveValue('ManualCorrection');
});
