import {defineConfig} from '@playwright/test';
import path from 'node:path';
const output=process.env.AUDIT_OUTPUT??path.resolve('../../../.build/audit-preview');
export default defineConfig({
  testDir:'tests/audit',workers:1,retries:0,timeout:45_000,
  outputDir:path.join(output,'browser-artifacts'),
  reporter:[['list'],['json',{outputFile:path.join(output,'browser.json')}],['html',{outputFolder:path.join(output,'browser-report'),open:'never'}]],
  use:{channel:'msedge',viewport:{width:1440,height:960},trace:'retain-on-failure',screenshot:'only-on-failure'},
});
