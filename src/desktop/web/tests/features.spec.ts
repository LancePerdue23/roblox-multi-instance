import {test,expect} from '@playwright/test';
import {installWorkspace} from './fixtures/workspace';

test.beforeEach(async({page})=>installWorkspace(page));

test('account profile bypass is explicit, persistent on refresh, and visible on its card',async({page})=>{
 await page.goto('/?accounts-demo');await page.getByRole('button',{name:'Accounts',exact:true}).click();
 await page.getByRole('button',{name:'View First account',exact:true}).click();
 const bypass=page.getByRole('checkbox',{name:'Bypass optimizations',exact:true});
 await expect(bypass).not.toBeChecked();await bypass.check();await expect(bypass).toBeChecked();
 await expect(page.locator('[data-account-id="first-local"]')).toContainText('Bypass enabled');
 const writes=await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='accounts.optimizationBypass'));
 expect(writes.map((call:any)=>call.payload)).toEqual([{id:'first-local',bypass:true}]);
 await page.getByRole('button',{name:'Play',exact:true}).click();await page.getByRole('button',{name:'Accounts',exact:true}).click();
 await page.getByRole('button',{name:'View First account',exact:true}).click();await expect(bypass).toBeChecked();
 await bypass.uncheck();await expect(page.locator('[data-account-id="first-local"]')).not.toContainText('Bypass enabled');
});

test('account rows highlight fully and keep status and group columns aligned',async({page})=>{
 await page.goto('/?accounts-demo');await page.getByRole('button',{name:'Accounts',exact:true}).click();
 await page.getByRole('button',{name:'Manage groups',exact:true}).click();
 await page.getByLabel('Select account Second account').check();
 await page.getByLabel('Group name',{exact:true}).fill('A much longer group name');
 await page.getByRole('button',{name:'Assign selected to group',exact:true}).click();
 await page.getByRole('button',{name:'Manage groups',exact:true}).click();
 const rows=page.locator('.account-row');
 const positions=await rows.evaluateAll(nodes=>nodes.map(row=>{
  const status=row.querySelector('.account-status')!.getBoundingClientRect(),group=row.querySelector('.account-group-cell')!.getBoundingClientRect();
  return {status:status.x,group:group.x};
 }));
 expect(positions[0]).toEqual(positions[1]);
 await rows.first().getByRole('button',{name:'View First account',exact:true}).hover();
 const highlighted=await rows.first().evaluate(row=>getComputedStyle(row).backgroundColor);
 expect(highlighted).not.toBe('rgba(0, 0, 0, 0)');
 expect(await rows.first().locator('.account-identity').evaluate(node=>getComputedStyle(node).backgroundColor)).toBe('rgba(0, 0, 0, 0)');
 await expect(rows.first().locator('.account-status')).toBeEmpty();
 await rows.first().locator('.account-group-cell').hover();
 expect(await rows.first().evaluate(row=>getComputedStyle(row).backgroundColor)).toBe(highlighted);
 await page.screenshot({path:'../../../.build/accounts-row-alignment.png',fullPage:true});
 await page.setViewportSize({width:900,height:960});
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
});

test('Play groups filter accounts and select or deselect a group without disturbing other launch slots',async({page})=>{
 await page.goto('/?accounts-demo');await page.getByRole('button',{name:'Play',exact:true}).click();
 await page.getByLabel('Select account Second account').check();
 const groups=page.getByRole('group',{name:'Play account groups',exact:true});
 await groups.getByRole('button',{name:'Main 1',exact:true}).click();
 await expect(page.getByLabel('Select account Second account')).toHaveCount(0);
 await page.getByRole('button',{name:'Select group',exact:true}).click();
 await expect(page.getByText('Slot 1: Second account',{exact:true})).toBeVisible();
 await expect(page.getByText('Slot 2: First account',{exact:true})).toBeVisible();
 await page.getByRole('button',{name:'Deselect group',exact:true}).click();
 await expect(page.getByText('Slot 1: Second account',{exact:true})).toBeVisible();
 await groups.getByRole('button',{name:'All 2',exact:true}).click();
 await expect(page.getByLabel('Select account Second account')).toBeChecked();
 await expect(page.getByLabel('Select account First account')).not.toBeChecked();
 await page.getByRole('button',{name:'Select all accounts',exact:true}).click();
 await expect(page.getByLabel('Select account First account')).toBeChecked();
 await page.getByRole('button',{name:'Accounts',exact:true}).click();
 await expect(page.getByLabel('Select account First account')).not.toBeChecked();
});

test('Accounts separates profile management from Play and preserves both selections',async({page})=>{
 await page.goto('/?accounts-demo&games-demo');
 await page.getByRole('button',{name:'Accounts',exact:true}).click();
 await expect(page.getByRole('heading',{name:'Recent games',exact:true})).toHaveCount(0);
 await expect(page.getByText('2 total',{exact:true})).toBeVisible();
 await page.getByLabel('Select account Second account').check();
 await page.getByRole('button',{name:'View First account',exact:true}).click();
 const profile=page.getByRole('complementary',{name:'Account details'});
 await expect(profile.getByText('@FixtureOne',{exact:true})).toBeVisible();
 await expect(profile.getByText('First notes',{exact:true})).toBeVisible();
 await profile.getByRole('button',{name:'Organize',exact:true}).click();
 await profile.getByRole('textbox',{name:'Notes',exact:true}).fill('Updated account notes');
 await profile.getByRole('button',{name:'Save account details',exact:true}).click();
 await expect(profile.getByRole('textbox',{name:'Notes',exact:true})).toHaveCount(0);
 await expect(profile.getByText('Updated account notes',{exact:true})).toBeVisible();
 await page.getByRole('group',{name:'Account groups',exact:true}).getByRole('button',{name:'Main 1',exact:true}).click();
 await expect(page.locator('.account-row')).toHaveCount(1);
 await page.getByRole('button',{name:'Play',exact:true}).click();
 await expect(page.getByRole('heading',{name:'Account library'})).toHaveCount(0);
 await expect(page.getByRole('heading',{name:'Recent games',exact:true})).toBeVisible();
 await expect(page.getByLabel('Select account Second account')).not.toBeChecked();
 await page.getByLabel('Select account First account').check();
 await page.getByLabel('Game URL or Place ID',{exact:true}).fill('123456');
 await page.getByRole('button',{name:'Accounts',exact:true}).click();
 await page.getByRole('group',{name:'Account groups',exact:true}).getByRole('button',{name:'All 2',exact:true}).click();
 await expect(page.getByLabel('Select account Second account')).toBeChecked();
 await expect(profile.getByText('Updated account notes',{exact:true})).toBeVisible();
 await page.screenshot({path:'../../../.build/accounts-management.png',fullPage:true});
 await page.getByRole('button',{name:'Play',exact:true}).click();
 await expect(page.getByLabel('Select account First account')).toBeChecked();
 await expect(page.getByLabel('Game URL or Place ID',{exact:true})).toHaveValue('123456');
 await page.screenshot({path:'../../../.build/play-workspace.png',fullPage:true});
});

test('account batch survives navigation and retains its result',async({page})=>{
 await page.goto('/?accounts-demo&launch-testing');
 await page.getByRole('button',{name:'Play',exact:true}).click();
 await page.evaluate(()=>{
  const view=window.chrome!.webview!,original=view.postMessage.bind(view);
  view.postMessage=(message:any)=>{
   if(message.command==='accounts.launchBatch'){
    (window as any).pendingBatch=message;
    return;
   }
   original(message);
  };
 });
 await page.getByLabel('Select account First account').check();
 await page.getByLabel('Select account Second account').check();
 await page.getByLabel('Game URL or Place ID',{exact:true}).fill('123456');
 await page.getByRole('button',{name:'Launch selected',exact:true}).click();
 await page.getByRole('dialog').getByRole('button',{name:'Launch selected accounts',exact:true}).click();
 await expect.poll(()=>page.evaluate(()=>!!(window as any).pendingBatch)).toBe(true);
 await page.getByRole('button',{name:'System',exact:true}).click();await page.getByRole('tab',{name:'Activity',exact:true}).click();
 await page.getByRole('button',{name:'Instances',exact:true}).click();
 await page.getByRole('button',{name:'Play',exact:true}).click();
 await expect(page.getByRole('button',{name:'Cancel pending launches',exact:true})).toBeVisible();
 expect(await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='cancel'))).toHaveLength(0);
 await page.evaluate(()=>{
  const message=(window as any).pendingBatch;
  (window as any).testEmit({version:1,id:message.id,data:{batch:{batchId:'navigation-batch',status:'Partial',canApplyLayout:false,items:[{accountId:'first-local',slot:1,status:'Unknown',code:'ACCOUNT_JOIN_UNCONFIRMED'}]},layoutStatus:'No automatic layout selected.'}});
 });
 await expect(page.getByText('Roblox may have opened, but the selected account could not be confirmed.',{exact:false})).toBeVisible();
 await expect(page.getByLabel('Select account Second account')).toBeChecked();
});

test('a launch requiring sign-in appears in the account summary and status filter',async({page})=>{
 await page.goto('/?accounts-demo&launch-testing');
 await page.getByRole('button',{name:'Play',exact:true}).click();
 await page.evaluate(()=>{
  const view=window.chrome!.webview!,original=view.postMessage.bind(view);
  view.postMessage=(message:any)=>{
   if(message.command==='accounts.launchBatch'){
    (window as any).testEmit({version:1,id:message.id,data:{batch:{batchId:'sign-in-needed',status:'Partial',canApplyLayout:false,items:[{accountId:'second-local',slot:1,status:'NeedsSignIn',code:'ACCOUNT_SESSION_EXPIRED'}]},layoutStatus:'No automatic layout selected.'}});return;
   }
   original(message);
  };
 });
 await page.getByLabel('Select account Second account').check();
 await page.getByLabel('Game URL or Place ID',{exact:true}).fill('123456');
 await page.getByRole('button',{name:'Launch selected',exact:true}).click();
 await page.getByRole('dialog').getByRole('button',{name:'Launch selected accounts',exact:true}).click();
 await expect(page.getByText('Batch: Partial',{exact:true})).toBeVisible();
 await page.getByRole('button',{name:'Accounts',exact:true}).click();
 await expect(page.getByText('1 known to need sign-in',{exact:true})).toBeVisible();
 await page.getByRole('combobox',{name:'Filter account status'}).click();
 await page.getByRole('option',{name:'Needs sign-in',exact:true}).click();
 await expect(page.locator('.account-row')).toHaveCount(1);
 await page.getByRole('button',{name:'View Second account',exact:true}).click();
 await expect(page.getByRole('complementary',{name:'Account details'}).getByRole('button',{name:'Reconnect',exact:true})).toBeEnabled();
});

test('saved account launch requires confirmation and explains an unconfirmed join without retry',async({page})=>{
 await page.goto('/?accounts-demo&launch-testing');await page.getByRole('button',{name:'Play',exact:true}).click();
 await page.getByLabel('Select account First account').check();await page.getByLabel('Game URL or Place ID',{exact:true}).fill('123456');
 await page.getByRole('button',{name:'Launch selected',exact:true}).click();await expect(page.getByRole('dialog')).toContainText('Verify the selected account');
 await page.getByRole('button',{name:'Cancel',exact:true}).click();
 expect(await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='accounts.launchBatch'))).toHaveLength(0);
 await page.getByRole('button',{name:'Launch selected',exact:true}).click();await page.getByRole('dialog').getByRole('button',{name:'Launch selected accounts',exact:true}).click();
 await expect(page.getByText('Roblox may have opened, but the selected account could not be confirmed.',{exact:false})).toBeVisible();
 const calls=await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='accounts.launchBatch'));
 expect(calls).toHaveLength(1);expect(calls[0].payload.ids).toEqual(['first-local']);
});

test('closing sign-in without an account releases controls and allows another attempt',async({page})=>{
 await page.getByRole('button',{name:'Accounts',exact:true}).click();
 const add=page.getByRole('button',{name:'Add Roblox account…',exact:true});await expect(add).toBeEnabled();
 await add.click();await expect(add).toBeEnabled();await add.click();await expect(add).toBeEnabled();
 await page.getByRole('button',{name:'Layouts',exact:true}).click();await page.getByRole('button',{name:'Accounts',exact:true}).click();await expect(add).toBeEnabled();
 expect(await page.evaluate(()=>(window as unknown as {testCalls:{command:string}[]}).testCalls.filter(c=>c.command==='accounts.connect').length)).toBe(2);
});

test('reporting is visibly optional and changing it never sends a report',async({page})=>{
 await page.getByRole('button',{name:'Settings',exact:true}).click();await page.getByRole('tab',{name:'Privacy',exact:true}).click();
 const toggle=page.getByLabel('Share optional technical error reports');await expect(toggle).not.toBeChecked();
 await expect(page.getByText('The reporting service is not configured in this build. Nothing can be uploaded.')).toBeVisible();
 await toggle.check();await expect(toggle).toBeChecked();await toggle.uncheck();await expect(toggle).not.toBeChecked();
 const calls=await page.evaluate(()=>(window as unknown as {testCalls:{command:string;payload:unknown}[]}).testCalls);
 expect(calls.filter(c=>c.command==='telemetry.set').map(c=>c.payload)).toEqual([{enabled:true},{enabled:false}]);
 expect(calls.filter(c=>c.command==='telemetry.send')).toHaveLength(0);
 await expect(page.getByRole('button',{name:'Send queued reports'})).toBeDisabled();
 await page.screenshot({path:'../../../.build/desktop-ui-results/privacy.png',fullPage:true});
});

test('support stays optional and disabled until configured without starting checkout',async({page})=>{
 await page.getByRole('button',{name:'Support development',exact:true}).click();
 await expect(page.getByRole('heading',{name:'Support development'})).toBeVisible();await expect(page.getByRole('button',{name:'Support with USD…'})).toBeDisabled();
 await expect(page.getByRole('button',{name:'Buy pass'})).toBeDisabled();
 await expect(page.getByText('Robux support is unavailable in this build.')).toBeVisible();
 await expect(page.getByText('Optional support. Every feature stays free.',{exact:false})).toBeVisible();
 expect(await page.evaluate(()=>(window as unknown as {testCalls:{command:string}[]}).testCalls.filter(c=>['support.start','support.claim','support.roblox'].includes(c.command)))).toHaveLength(0);
 await page.screenshot({path:'../../../.build/desktop-ui-results/support.png',fullPage:true});
});

test('Robux donation is independent of Stripe and never claims a supporter badge',async({page})=>{
 await page.goto('/?roblox-ready');await page.getByRole('button',{name:'Support development',exact:true}).click();
 await expect(page.getByRole('button',{name:'Support with USD…'})).toBeDisabled();
 await expect(page.getByRole('button',{name:'Buy pass'})).toBeEnabled();
 await expect(page.getByText('Verify purchases finds support passes across all your connected accounts and updates your total automatically.',{exact:false})).toBeVisible();
 await page.getByRole('button',{name:'Buy pass'}).click();
 await expect.poll(()=>page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='support.roblox').length)).toBe(1);
 expect(await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>['support.start','support.claim'].includes(c.command)))).toHaveLength(0);
 await expect(page.getByLabel('Show my supporter badge')).toBeDisabled();
});

test('configured Stripe starts only after an explicit click while Robux remains unavailable',async({page})=>{
 await page.goto('/?stripe-ready');await page.getByRole('button',{name:'Support development',exact:true}).click();await page.getByText('USD contributions and recovery',{exact:true}).click();
 await expect(page.getByRole('button',{name:'Support with USD…'})).toBeEnabled();
 await expect(page.getByRole('button',{name:'Buy pass'})).toBeDisabled();
 expect(await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='support.start'))).toHaveLength(0);
 await page.getByRole('button',{name:'Support with USD…'}).click();
 await expect.poll(()=>page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='support.start').length)).toBe(1);
 await expect(page.getByLabel('Show my supporter badge')).toBeDisabled();
});

test('verified cosmetic badge can be hidden and removed without changing launch access',async({page})=>{
 await page.goto('/?supporter-verified');await expect(page.locator('.sidebar-supporter')).toBeVisible();
 await page.locator('.sidebar-bottom').screenshot({path:'../../../.build/supporter-card.png'});
 await expect(page.getByRole('button',{name:'Launch games',exact:true})).toBeEnabled();
 await page.getByRole('button',{name:'Support development',exact:true}).click();
 await page.getByText('Support settings and details',{exact:true}).click();
 await page.getByLabel('Show my supporter badge').uncheck();await expect(page.locator('.sidebar-supporter')).toHaveCount(0);
 await page.getByText('Stripe receipt backups',{exact:true}).click();await page.getByRole('button',{name:'Remove receipt…'}).click();await expect(page.getByRole('dialog')).toContainText('Export it first');
 expect(await page.evaluate(()=>(window as unknown as {testCalls:{command:string}[]}).testCalls.filter(c=>c.command==='support.remove'))).toHaveLength(0);
 await page.getByRole('button',{name:'Remove supporter receipt',exact:true}).click();await expect(page.getByLabel('Show my supporter badge')).toBeDisabled();
 await expect(page.getByRole('button',{name:'Launch games',exact:true})).toBeEnabled();
});

test('account groundwork preserves selection order and clearly gates automatic launch',async({page})=>{
 await page.goto('/?accounts-demo');await page.getByRole('button',{name:'Accounts',exact:true}).click();
 await expect(page.getByRole('button',{name:'Add Roblox account…'})).toBeEnabled();
 await page.screenshot({path:'../../../.build/desktop-ui-results/accounts.png',fullPage:true});
 await page.getByRole('button',{name:'Play',exact:true}).click();await page.getByLabel('Select account Second account').check();await page.getByLabel('Select account First account').check();
 await expect(page.getByText('Slot 1: Second account',{exact:true})).toBeVisible();await expect(page.getByText('Slot 2: First account',{exact:true})).toBeVisible();
 await page.getByRole('button',{name:'Move account 2 up',exact:true}).click();await expect(page.getByText('Slot 1: First account',{exact:true})).toBeVisible();
 await page.getByLabel('Game URL or Place ID',{exact:true}).fill('123456');await page.getByRole('button',{name:'Check game target'}).click();await expect(page.getByText('Place 123456',{exact:true})).toBeVisible();
 await expect(page.getByRole('button',{name:'Launch selected',exact:true})).toBeDisabled();await expect(page.getByText('Launch selected is disabled in this build, even after successful sign-in and game-target validation.',{exact:false})).toBeVisible();
 await expect(page.getByText('Saved accounts are not used for browser launches.',{exact:false})).toBeVisible();
 expect(await page.evaluate(()=>(window as unknown as {testCalls:{command:string}[]}).testCalls.filter(c=>c.command==='accounts.launchBatch'))).toHaveLength(0);
 await page.screenshot({path:'../../../.build/desktop-ui-results/accounts-groundwork.png',fullPage:true});
});

test('removing a saved account requires confirmation before any session deletion',async({page})=>{
 await page.goto('/?accounts-demo');await page.getByRole('button',{name:'Accounts',exact:true}).click();await page.getByRole('button',{name:'View First account',exact:true}).click();await page.getByRole('button',{name:'Remove…',exact:true}).click();
 await expect(page.getByRole('dialog')).toContainText('Already running games will stay open.');await page.getByRole('button',{name:'Cancel',exact:true}).click();
 expect(await page.evaluate(()=>(window as unknown as {testCalls:{command:string}[]}).testCalls.filter(c=>c.command==='accounts.remove'))).toHaveLength(0);
 await page.getByRole('button',{name:'Remove…',exact:true}).first().click();await page.getByRole('button',{name:'Remove account',exact:true}).click();await expect(page.getByText('FixtureOne · Saved locally',{exact:true})).toHaveCount(0);
});

test('account favorites move cards, preserve selection and respect reduced motion',async({page})=>{
 await page.goto('/?accounts-demo');await page.getByRole('button',{name:'Accounts',exact:true}).click();
 const cards=page.locator('.account-row');await expect(cards.first()).toHaveAttribute('data-account-id','first-local');
 await page.getByLabel('Select account Second account').check();
 await expect(page.getByRole('button',{name:/Move saved account/})).toHaveCount(0);await expect(page.getByText('First notes',{exact:true})).toHaveCount(0);
 await page.getByRole('button',{name:'Favorite Second account',exact:true}).click();
 await expect(page.getByRole('button',{name:'Unfavorite Second account',exact:true})).toHaveAttribute('aria-pressed','true');
 await page.getByRole('button',{name:'Unfavorite First account',exact:true}).click();
 await expect(cards.first()).toHaveAttribute('data-account-id','second-local');
 await expect(page.getByLabel('Select account Second account')).toBeChecked();
 await page.getByRole('button',{name:'Play',exact:true}).click();await expect(page.getByLabel('Select account Second account')).not.toBeChecked();await page.getByRole('button',{name:'Accounts',exact:true}).click();
 await page.getByLabel('Favorites only').check();await expect(cards).toHaveCount(1);await page.getByLabel('Favorites only').uncheck();
 await page.emulateMedia({reducedMotion:'reduce'});await page.getByRole('button',{name:'Unfavorite Second account',exact:true}).click();await expect(cards.first()).toHaveAttribute('data-account-id','first-local');
 expect(await cards.evaluateAll(nodes=>nodes.reduce((count,node)=>count+node.getAnimations().length,0))).toBe(0);
 const calls=await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='accounts.organize'));
 expect(calls.find((c:any)=>c.payload.id==='first-local').payload.notes).toBe('First notes');
 await page.getByRole('button',{name:'Play',exact:true}).click();await expect(page.getByRole('button',{name:'Launch selected',exact:true})).toBeDisabled();
});

test('groups can be assigned, renamed and removed without removing accounts',async({page})=>{
 await page.goto('/?accounts-demo');await page.getByRole('button',{name:'Accounts',exact:true}).click();
 await page.getByRole('button',{name:'Manage groups',exact:true}).click();await page.getByLabel('Select account Second account').check();
 await page.getByLabel('Group name',{exact:true}).fill('Team');await page.getByRole('button',{name:'Assign selected to group',exact:true}).click();
 await expect(page.getByRole('button',{name:'Remove group Team',exact:true})).toBeVisible();
 await page.getByLabel('Group name',{exact:true}).fill('Squad');await page.getByRole('button',{name:'Rename group Team',exact:true}).click();
 await expect(page.getByRole('button',{name:'Remove group Squad',exact:true})).toBeVisible();await expect(page.getByRole('button',{name:'Remove group Team',exact:true})).toHaveCount(0);
 await page.getByRole('button',{name:'Remove group Squad',exact:true}).click();await page.getByRole('dialog').getByRole('button',{name:'Remove group',exact:true}).click();
 await expect(page.getByRole('button',{name:'Remove group Squad',exact:true})).toHaveCount(0);await expect(page.locator('.account-row')).toHaveCount(2);
 const calls=await page.evaluate(()=>(window as any).testCalls);expect(calls.filter((c:any)=>c.command==='accounts.remove')).toHaveLength(0);
 expect(calls.filter((c:any)=>c.command==='accounts.organize').map((c:any)=>c.payload.group)).toEqual(['Team','Squad','']);
});


test('a verified gamepass updates the whole app supporter badge without enabling Stripe export',async({page})=>{
 await page.goto('/?roblox-ready&roblox-owned');
 await page.getByRole('button',{name:'Support development',exact:true}).click();
 await page.getByRole('button',{name:'Verify purchases',exact:true}).click();
 await expect(page.locator('.sidebar-supporter')).toBeVisible();
 await page.getByRole('button',{name:'Continue',exact:true}).click();await page.getByText('Support settings and details',{exact:true}).click();await page.getByText('Stripe receipt backups',{exact:true}).click();
 await expect(page.getByRole('button',{name:'Export receipt…',exact:true})).toBeDisabled();
 await expect(page.getByRole('button',{name:'Remove receipt…',exact:true})).toBeDisabled();
 await page.locator('.sidebar-bottom').screenshot({path:'../../../.build/supporter-card.png'});
 await page.getByLabel('Show my supporter badge').uncheck();
 await expect(page.locator('.sidebar-supporter')).toHaveCount(0);
 await expect(page.getByRole('button',{name:'Support development',exact:true})).toBeVisible();
});

test('profile name edits a local nickname and account creation sits in the header',async({page})=>{
 await page.goto('/?accounts-demo');await page.getByRole('button',{name:'Accounts',exact:true}).click();
 await expect(page.getByLabel('Local nickname (optional)',{exact:true})).toHaveCount(0);
 await expect(page.locator('.card-heading').getByRole('button',{name:'Add Roblox account…'})).toBeVisible();
 await page.getByRole('button',{name:'View First account',exact:true}).click();
 const profile=page.getByRole('complementary',{name:'Account details'});
 await expect(profile.getByText('Click the name to edit your local nickname.',{exact:true})).toBeVisible();
 await profile.getByRole('button',{name:'Edit nickname for FixtureOne'}).click();
 await profile.getByLabel('Local nickname',{exact:true}).fill('Temporary name');await page.keyboard.press('Escape');
 await expect(page.getByRole('button',{name:'View First account',exact:true})).toBeVisible();
 await profile.getByRole('button',{name:'Edit nickname for FixtureOne'}).click();
 const field=profile.getByLabel('Local nickname',{exact:true});await field.fill('   ');await expect(profile.getByRole('button',{name:'Save nickname'})).toBeDisabled();
 await field.fill('My main account');await field.press('Enter');
 await expect(page.getByRole('button',{name:'View My main account',exact:true})).toBeVisible();
 await expect(profile.getByText('@FixtureOne',{exact:true})).toBeVisible();
 await profile.getByRole('button',{name:'Organize',exact:true}).click();await expect(profile.getByLabel('Local nickname',{exact:true})).toHaveCount(0);
 await page.screenshot({path:'../../../.build/accounts-inline-nickname.png',fullPage:true});
});
