import {test,expect} from '@playwright/test';
import {installWorkspace} from './fixtures/workspace';
test.beforeEach(async({page})=>installWorkspace(page));

test('recent Play repeats saved accounts despite current selection and artwork refresh',async({page})=>{
 await page.goto('/?accounts-demo&launch-testing&games-demo');
 await page.getByRole('button',{name:'Play',exact:true}).click();
 const recent=page.getByRole('heading',{name:'Recent games',exact:true}).locator('xpath=ancestor::section');
 await expect(recent.getByRole('img',{name:'Cover art for Fixture Adventure'})).toBeVisible();
 const play=recent.getByRole('button',{name:'Play Fixture Adventure again'});
 await expect(play).toBeEnabled();
 await expect(recent.getByText('Accounts: Second account, First account')).toBeVisible();
 await page.getByLabel('Select account First account').check();
 await page.getByLabel('Game URL or Place ID',{exact:true}).fill('123456');
 await page.getByRole('button',{name:'Check game target',exact:true}).click();
 await expect(page.getByRole('heading',{name:'Fixture Adventure',exact:true})).toHaveCount(2);
 if(!process.env.CI)await recent.screenshot({path:'../../../.build/recent-games.png'});
 await page.setViewportSize({width:900,height:960});
 if(!process.env.CI)await recent.screenshot({path:'../../../.build/recent-games-narrow.png'});
 await page.setViewportSize({width:1440,height:960});
 await page.getByLabel('Public server Job ID (optional)').fill('aa112233-4455-6677-8899-aabbccddeeff');
 await play.click();
 await expect(page.getByText('Batch: Partial',{exact:true})).toBeVisible();
 const calls=await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='accounts.launchBatch'));
 expect(calls).toHaveLength(1);expect(calls[0].payload).toMatchObject({ids:['second-local','first-local'],target:'123456',jobId:''});
 await expect(page.getByRole('dialog')).not.toBeVisible();
 await recent.getByRole('button',{name:'Remove Fixture Adventure from recent games'}).click();
 await expect(recent.getByRole('img')).toHaveCount(0);
});

test('Change accounts prepares the saved batch without launching',async({page})=>{
 await page.goto('/?accounts-demo&launch-testing&games-demo');
 await page.getByRole('button',{name:'Play',exact:true}).click();
 await page.getByRole('button',{name:'Change accounts for Fixture Adventure'}).click();
 await expect(page.getByRole('heading',{name:'Choose accounts',exact:true})).toBeFocused();
 await expect(page.getByRole('heading',{name:'Choose accounts',exact:true})).toBeInViewport();
 await expect(page.getByRole('group',{name:'Choose accounts for this game'})).toHaveCount(0);
 await expect(page.getByLabel('Select account Second account')).toBeChecked();
 await expect(page.getByLabel('Select account First account')).toBeChecked();
 await expect(page.getByLabel('Game URL or Place ID',{exact:true})).toHaveValue('123456');
 expect(await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='accounts.launchBatch'))).toHaveLength(0);
 await page.getByLabel('Select account Second account').uncheck();
 await page.getByRole('button',{name:'Launch selected',exact:true}).click();
 await page.getByRole('dialog').getByRole('button',{name:'Launch selected accounts',exact:true}).click();
 await expect(page.getByText('Batch: Partial',{exact:true})).toBeVisible();
 const calls=await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='accounts.launchBatch'));
 expect(calls[0].payload).toMatchObject({ids:['first-local'],target:'123456',jobId:'',preset:''});
});

for(const scenario of ['legacy-game','missing-game-account'])test(`${scenario} requires choosing accounts before replay`,async({page})=>{
 await page.goto('/?accounts-demo&launch-testing&games-demo&'+scenario);
 await page.getByRole('button',{name:'Play',exact:true}).click();
 await expect(page.getByRole('button',{name:'Play Fixture Adventure again'})).toBeDisabled();
 await page.getByRole('button',{name:'Change accounts for Fixture Adventure'}).click();
 await expect(page.getByLabel('Select account First account')).not.toBeChecked();
 await expect(page.getByRole('button',{name:'Launch selected',exact:true})).toBeDisabled();
});

test('checked target survives navigation and editing clears the old game card',async({page})=>{
 await page.goto('/?accounts-demo&launch-testing');await page.getByRole('button',{name:'Play',exact:true}).click();
 await page.evaluate(()=>{const view=window.chrome!.webview!,original=view.postMessage.bind(view);view.postMessage=(message:any)=>{if(message.command==='accounts.target'){(window as any).targetRequest=message;return;}original(message);};});
 await page.getByLabel('Game URL or Place ID',{exact:true}).fill('123456');
 await page.getByRole('button',{name:'Check game target',exact:true}).click();
 // An edit from another UI update can invalidate a pending request as well.
 await page.getByRole('button',{name:'Instances',exact:true}).click();
 await page.getByRole('button',{name:'Play',exact:true}).click();
 await page.evaluate(()=>{const message=(window as any).targetRequest;(window as any).testEmit({version:1,id:message.id,data:{placeId:'123456',jobId:null,game:{placeId:'123456',name:'No Artwork Game',artwork:null}}});});
 await expect(page.getByLabel('Cover art unavailable')).toBeVisible();
 await page.getByLabel('Game URL or Place ID',{exact:true}).fill('987654');
 await expect(page.getByRole('heading',{name:'No Artwork Game'})).toHaveCount(0);
});


test('cancel changing recent accounts restores the draft and returns without launching',async({page})=>{
 await page.goto('/?accounts-demo&launch-testing&games-demo');
 await page.getByRole('button',{name:'Play',exact:true}).click();
 await page.getByLabel('Select account First account').check();
 await page.getByLabel('Game URL or Place ID',{exact:true}).fill('987654');
 const groups=page.getByRole('group',{name:'Play account groups',exact:true});
 await groups.getByRole('button',{name:'Main 1',exact:true}).click();
 await page.getByRole('button',{name:'Change accounts for Fixture Adventure'}).click();
 await expect(page.getByLabel('Select account Second account')).toBeChecked();
 await page.getByLabel('Select account First account').uncheck();
 await page.getByRole('button',{name:'Cancel changes',exact:true}).first().click();
 await expect(page.getByRole('heading',{name:'Recent games',exact:true})).toBeFocused();
 await expect(page.getByRole('button',{name:'Cancel changes',exact:true})).toHaveCount(0);
 await expect(page.getByLabel('Game URL or Place ID',{exact:true})).toHaveValue('987654');
 await expect(groups.getByRole('button',{name:'Main 1',exact:true})).toHaveAttribute('aria-pressed','true');
 await groups.getByRole('button',{name:'All 2',exact:true}).click();
 await expect(page.getByLabel('Select account First account')).toBeChecked();
 await expect(page.getByLabel('Select account Second account')).not.toBeChecked();
 expect(await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='accounts.launchBatch'))).toHaveLength(0);
 await page.getByRole('button',{name:'Change accounts for Fixture Adventure'}).click();
 await expect(page.getByLabel('Select account Second account')).toBeChecked();
 await expect(page.getByLabel('Select account First account')).toBeChecked();
 await expect(page.getByRole('heading',{name:'Choose accounts',exact:true})).toBeInViewport();
 if(!process.env.CI)await page.screenshot({path:'../../../.build/change-accounts.png',fullPage:true});
});
