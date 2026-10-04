import { readFileSync } from 'node:fs';
import { FLOW_STATE } from './playwright.config';

export interface FlowState {
  publicId: string;
  publicPath: string;
  title: string;
}

/** The event create-publish.spec.ts published in this run. */
export function readFlowState(): FlowState {
  return JSON.parse(readFileSync(FLOW_STATE, 'utf8')) as FlowState;
}
