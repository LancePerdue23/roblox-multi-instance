import {defineConfig} from '@playwright/test';
export default defineConfig({testDir:'tests',outputDir:'../../.build/preview-ui-results',workers:1,use:{channel:'msedge',baseURL:'https://preview-fixture.invalid',viewport:{width:1440,height:960},trace:'retain-on-failure'}});

