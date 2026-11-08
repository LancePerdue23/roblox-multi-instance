import {test,expect,type Page} from '@playwright/test';
import {installWorkspace} from './fixtures/workspace';

test.beforeEach(async({page})=>installWorkspace(page));
const launches=(page:Page)=>page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='accounts.launchBatch'));
async function open(page:Page,query='accounts-demo&launch-testing&games-demo&grid-game'){
 await page.goto('/?'+query);await page.getByRole('button',{name:'Launch games',exact:true}).click();
 await expect(page.getByRole('dialog').getByRole('button',{name:'New launch →',exact:true})).toBeEnabled();
 return page.getByRole('dialog');
}

test('repeat uses persisted account order and Grid, reviews before launch, and reports uncertainty honestly',async({page})=>{
 const panel=await open(page);await expect(panel).toContainText('Fixture Adventure');await expect(panel).toContainText('Second account · First account');
 await panel.screenshot({path:'../../../.build/launch-panel-home.png'});
 await panel.getByRole('button',{name:'↻ Repeat last launch',exact:true}).click();
 await expect(panel.getByRole('heading',{name:'Ready to launch?'})).toBeFocused();
 await expect(panel).toContainText('Grid layout');expect(await launches(page)).toHaveLength(0);
 await panel.screenshot({path:'../../../.build/launch-panel-review.png'});
 await panel.getByRole('button',{name:'Launch 2 accounts',exact:true}).click();
 await expect(panel).toContainText('Batch: Partial');await expect(panel).toContainText('Check Instances and the game before retrying');
 expect((await launches(page))[0].payload).toEqual({ids:['second-local','first-local'],target:'123456',jobId:'',preset:'',arrangement:'Grid'});
 await panel.getByRole('button',{name:'View instances →'}).click();await expect(panel).not.toBeVisible();await expect(page.getByRole('heading',{name:'Instances',exact:true})).toBeVisible();
});

test('editing a repeat is harmless until confirmed and does not overwrite the Play draft',async({page})=>{
 await page.goto('/?accounts-demo&launch-testing&games-demo');await page.getByRole('button',{name:'Play',exact:true}).click();
 await page.getByLabel('Select account First account').check();await page.getByLabel('Game URL or Place ID',{exact:true}).fill('987654');
 await page.getByRole('button',{name:'Launch games',exact:true}).click();const panel=page.getByRole('dialog');
 await panel.getByRole('button',{name:'Edit setup',exact:true}).click();await expect(panel.getByLabel('Game URL or Place ID',{exact:true})).toHaveValue('123456');
 await panel.getByRole('button',{name:'Move launch account 2 up',exact:true}).click();await expect(panel).toContainText('Slot 1: First account');
 await panel.getByRole('button',{name:'Review launch →',exact:true}).click();await page.keyboard.press('Escape');
 expect(await launches(page)).toHaveLength(0);await expect(page.getByLabel('Game URL or Place ID',{exact:true})).toHaveValue('987654');
 await expect(page.getByLabel('Select account First account')).toBeChecked();await expect(page.getByLabel('Select account Second account')).not.toBeChecked();
});

test('new launch validates target and supports ordered accounts and Columns from any page',async({page})=>{
 await page.goto('/?accounts-demo&launch-testing');await page.getByRole('button',{name:'Settings',exact:true}).click();await page.getByRole('button',{name:'Launch games',exact:true}).click();
 const panel=page.getByRole('dialog');await panel.getByRole('button',{name:'New launch →',exact:true}).click();
 const review=panel.getByRole('button',{name:'Review launch →',exact:true});await expect(review).toBeDisabled();
 await panel.getByLabel('Game URL or Place ID',{exact:true}).fill('https://www.roblox.com/games/123456/Example');
 await panel.getByRole('checkbox',{name:'First account',exact:false}).check();await panel.getByRole('checkbox',{name:'Second account',exact:false}).check();
 await panel.getByLabel('Quick launch layout').selectOption('Columns');await review.click();await expect(panel).toContainText('Columns layout');
 await panel.getByRole('button',{name:'Launch 2 accounts',exact:true}).click();await expect(panel).toContainText('Batch: Partial');
 expect((await launches(page))[0].payload).toMatchObject({ids:['first-local','second-local'],target:'123456',arrangement:'Columns',preset:''});
});

test('removed-account history cannot repeat and can be repaired explicitly',async({page})=>{
 const panel=await open(page,'accounts-demo&launch-testing&games-demo&missing-game-account');
 await expect(panel.getByRole('button',{name:'↻ Repeat last launch',exact:true})).toBeDisabled();
 await panel.getByRole('button',{name:'Edit setup',exact:true}).click();await expect(panel.getByRole('button',{name:'Review launch →'})).toBeDisabled();
 await panel.getByRole('button',{name:'Remove unavailable account'}).click();await panel.getByRole('checkbox',{name:'First account',exact:false}).check();
 await panel.getByRole('button',{name:'Review launch →'}).click();await expect(panel.getByRole('button',{name:'Launch 1 account',exact:true})).toBeEnabled();expect(await launches(page)).toHaveLength(0);
});

test('pending launch survives hiding and supports cancellation without closing games',async({page})=>{
 const panel=await open(page);await panel.getByRole('button',{name:'↻ Repeat last launch'}).click();
 await page.evaluate(()=>{const view=window.chrome!.webview!,original=view.postMessage.bind(view);view.postMessage=(message:any)=>{if(message.command==='accounts.launchBatch'){(window as any).heldLaunch=message;(window as any).testCalls.push(message);return;}original(message);};});
 await panel.getByRole('button',{name:'Launch 2 accounts',exact:true}).dblclick();await expect(panel).toContainText('Launching your accounts…');
 await panel.getByRole('button',{name:'Hide launch panel'}).click();await page.getByRole('button',{name:'Launch games',exact:true}).click();await expect(panel).toContainText('Launching your accounts…');
 await panel.getByRole('button',{name:'Cancel pending launches',exact:true}).click();
 const calls=await page.evaluate(()=>(window as any).testCalls);expect(calls.filter((c:any)=>c.command==='accounts.launchBatch')).toHaveLength(1);expect(calls.some((c:any)=>c.command==='cancel'&&c.payload.operationId)).toBe(true);expect(calls.some((c:any)=>['instances.close','instances.stopAll'].includes(c.command))).toBe(false);
 await page.evaluate(()=>{const w=window as any;w.testEmit({version:1,id:w.heldLaunch.id,data:{batch:{batchId:'canceled',status:'Canceled',canApplyLayout:false,items:[]},layoutStatus:'Layout canceled.'}});});
 await expect(panel).toContainText('Batch: Canceled');
 await panel.getByRole('button',{name:'Close launch panel'}).click();await page.getByRole('button',{name:'Launch games',exact:true}).click();await expect(panel).toContainText('Batch: Canceled');
});

test('saved layout slot counts are enforced and switching to Grid repairs a mismatch',async({page})=>{
 await page.goto('/?accounts-demo&launch-testing&games-demo');
 await page.evaluate(()=>{const view=window.chrome!.webview!,original=view.postMessage.bind(view);view.postMessage=(message:any)=>{if(message.command==='layouts.get'){setTimeout(()=>(window as any).testEmit({version:1,id:message.id,data:{presets:[{name:'Two windows',slots:[{},{}]}],monitors:[],shortcuts:{}}}),0);return;}original(message);};});
 await page.getByRole('button',{name:'Launch games',exact:true}).click();const panel=page.getByRole('dialog');await panel.getByRole('button',{name:'Edit setup'}).click();
 await panel.getByLabel('Quick launch layout').selectOption('preset:Two windows');await panel.getByRole('checkbox',{name:'First account',exact:false}).uncheck();
 await expect(panel.getByRole('alert')).toContainText('needs 2 accounts');await expect(panel.getByRole('button',{name:'Review launch →'})).toBeDisabled();
 await panel.getByLabel('Quick launch layout').selectOption('Grid');await panel.getByRole('button',{name:'Review launch →'}).click();await expect(panel.getByRole('button',{name:'Launch 1 account',exact:true})).toBeEnabled();expect(await launches(page)).toHaveLength(0);
});

test('target rejection leaves setup editable and starts no games',async({page})=>{
 const panel=await open(page);await panel.getByRole('button',{name:'Edit setup'}).click();
 await page.evaluate(()=>{const view=window.chrome!.webview!,original=view.postMessage.bind(view);view.postMessage=(message:any)=>{if(message.command==='accounts.target'){setTimeout(()=>(window as any).testEmit({version:1,id:message.id,error:{code:'INVALID_TARGET',message:'Private-server links are not supported.'}}),0);return;}original(message);};});
 await panel.getByRole('button',{name:'Review launch →'}).click();await expect(panel.getByRole('alert')).toContainText('Private-server links are not supported');await expect(panel.getByLabel('Game URL or Place ID',{exact:true})).toBeEnabled();expect(await launches(page)).toHaveLength(0);
});

test('empty library links to Accounts and compact launcher fits without horizontal overflow',async({page})=>{
 const panel=await open(page,'');await expect(panel).toContainText('No recent launches yet');await page.setViewportSize({width:420,height:760});
 await panel.screenshot({path:'../../../.build/launch-panel-narrow.png'});
 expect(await panel.evaluate(el=>el.scrollWidth<=el.clientWidth)).toBe(true);
 await panel.getByRole('button',{name:'Add accounts…'}).click();await expect(page.getByRole('heading',{name:'Accounts',exact:true})).toBeVisible();
});
