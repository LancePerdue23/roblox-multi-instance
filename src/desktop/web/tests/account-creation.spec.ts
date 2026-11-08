import {test,expect} from '@playwright/test';
import {installWorkspace} from './fixtures/workspace';

test.beforeEach(async({page})=>installWorkspace(page));
test('creation saves account availability and sanitized progress, then links to Play',async({page})=>{
 await page.getByRole('button',{name:'Accounts',exact:true}).click();
 await page.getByRole('button',{name:'Create accounts…',exact:true}).click();
 await expect(page.getByText('Account creation: Finished · 1 completed · 0 skipped · 0 remaining')).toBeVisible();
 await page.getByRole('button',{name:'View Created account',exact:true}).click();
 await page.getByRole('button',{name:'View credentials…',exact:true}).click();
 const calls=await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>['accounts.create','accounts.credentials.open'].includes(c.command)));
 expect(calls.map((c:any)=>({command:c.command,payload:c.payload}))).toEqual([{command:'accounts.create',payload:{}},{command:'accounts.credentials.open',payload:{id:'created-local'}}]);
 await page.getByRole('button',{name:'Go to Play →',exact:true}).click();await expect(page.getByRole('heading',{name:'Choose accounts',exact:true})).toBeVisible();
});
test('unfinished signup recovery opens native controls without a credential bridge payload',async({page})=>{
 await page.getByRole('button',{name:'Accounts',exact:true}).click();
 await page.getByRole('button',{name:'Unfinished signups…',exact:true}).click();
 const calls=await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='accounts.credentials.open'));
 expect(calls.map((c:any)=>c.payload)).toEqual([{id:''}]);
});

for(const status of ['Canceled','Stopped','Skipped'])test(`creation ${status} reports its outcome without claiming an account was saved`,async({page})=>{
 await page.goto('/?creation-outcome='+status);await page.getByRole('button',{name:'Accounts',exact:true}).click();
 await page.getByRole('button',{name:'Create accounts…',exact:true}).click();
 await expect(page.getByText(new RegExp('Account creation: '+status))).toBeVisible();
 await expect(page.getByText('Ready to play',{exact:true})).toHaveCount(0);
 await expect(page.getByRole('button',{name:'View Created account',exact:true})).toHaveCount(0);
});

test('credential action reserves the same space on accounts without stored credentials',async({page})=>{
 await page.goto('/?accounts-demo');await page.getByRole('button',{name:'Accounts',exact:true}).click();
 await page.getByRole('button',{name:'View Second account',exact:true}).click();
 const reserved=await page.locator('.credential-action-placeholder').evaluate(e=>e.getBoundingClientRect().height);
 await expect(page.getByRole('button',{name:'View credentials…',exact:true})).toHaveCount(0);
 await page.getByRole('button',{name:'Create accounts…',exact:true}).click();
 await expect(page.getByText('1 account saved. Available in Play.')).toBeVisible();
 await page.getByRole('button',{name:'View Created account',exact:true}).click();
 const button=page.getByRole('button',{name:'View credentials…',exact:true});
 await expect(button).toBeVisible();expect(await button.evaluate(e=>e.getBoundingClientRect().height)).toBe(reserved);
});
