import { describe, expect, it } from 'vitest';
import { availableActions } from './merge';

const all = { request: true, approve: true, undo: true };

describe('merge actions', () => {
  it('offers approval only once clinician approval was requested, and undo only for a merge still within its window', () => {
    expect(availableActions({ status: 'Proposed', canUndo: false }, all)).toEqual(['notSame', 'requestApproval']);
    expect(availableActions({ status: 'AwaitingApproval', canUndo: false }, all)).toEqual(['notSame', 'approve']);
    expect(availableActions({ status: 'Merged', canUndo: true }, all)).toEqual(['undo']);
    expect(availableActions({ status: 'Merged', canUndo: false }, all)).toEqual([]);
    expect(availableActions({ status: 'NotSame', canUndo: false }, all)).toEqual([]);
  });

  it('offers a role only its own steps', () => {
    expect(availableActions({ status: 'AwaitingApproval', canUndo: false }, { request: true, approve: false, undo: false })).toEqual(['notSame']);
    expect(availableActions({ status: 'AwaitingApproval', canUndo: false }, { request: false, approve: true, undo: false })).toEqual(['approve']);
    expect(availableActions({ status: 'Merged', canUndo: true }, { request: true, approve: true, undo: false })).toEqual([]);
  });
});
