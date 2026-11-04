import { register } from 'node:module';

register('./largeStepStrategy.loader.mjs', import.meta.url);
export const { createPlaneGcsSolver } = await import('../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js');
