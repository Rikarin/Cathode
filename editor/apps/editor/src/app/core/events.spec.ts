import { computed } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { Telemetry } from '@cathode/api-client';
import { ConnectionService } from './connection';
import { EventStream, blockMeter, modulatedValue } from './events';

describe('blockMeter', () => {
  const frame = {
    type: 'telemetry',
    blocks: { in1: { peak: 0.8 }, cab1: { peak: 0.4, preampIn: -6, motorReduction: 1.5 } }
  } as unknown as Telemetry;

  it('reads a block peak and its own meters from the one blocks section', () => {
    expect(blockMeter(frame, 'in1', 'peak')).toBe(0.8);
    expect(blockMeter(frame, 'cab1', 'peak')).toBe(0.4);
    expect(blockMeter(frame, 'cab1', 'preampIn')).toBe(-6);
    expect(blockMeter(frame, 'cab1', 'motorReduction')).toBe(1.5);
  });

  it('is undefined before a frame, for an unknown block and for a meter the block does not have', () => {
    expect(blockMeter(null, 'cab1', 'peak')).toBeUndefined();
    expect(blockMeter(frame, 'delay1', 'peak')).toBeUndefined();
    expect(blockMeter(frame, 'in1', 'preampIn')).toBeUndefined();
  });
});

describe('modulatedValue', () => {
  const modulation = { delay1: { mix: 42.5, feedback: 10 }, amp1: { gain: 0 } };

  it('reads a modulated parameter by block and parameter id', () => {
    expect(modulatedValue(modulation, 'delay1', 'mix')).toBe(42.5);
    expect(modulatedValue(modulation, 'amp1', 'gain')).toBe(0);
  });

  it('is undefined when nothing modulates the parameter or the value is not finite', () => {
    expect(modulatedValue(null, 'delay1', 'mix')).toBeUndefined();
    expect(modulatedValue(modulation, 'delay1', 'time')).toBeUndefined();
    expect(modulatedValue(modulation, 'reverb1', 'mix')).toBeUndefined();
    expect(modulatedValue({ delay1: { mix: Number.NaN } }, 'delay1', 'mix')).toBeUndefined();
  });
});

describe('EventStream resync', () => {
  it('asks the stores to read their state again, as a (re)connection does, instead of passing it on as an event', () => {
    TestBed.configureTestingModule({ providers: [{ provide: ConnectionService, useValue: {} }] });
    const stream = TestBed.inject(EventStream);
    let resyncs = 0;
    const events: string[] = [];
    stream.connected$.subscribe(() => resyncs++);
    stream.events$.subscribe(e => events.push(e.type));

    (stream as unknown as { receive(data: unknown): void }).receive(JSON.stringify({ type: 'resync' }));

    expect(resyncs).toBe(1);
    expect(events).toEqual([]);
  });
});

describe('EventStream.modulation', () => {
  function receive(stream: EventStream, message: object): void {
    (stream as unknown as { receive(data: unknown): void }).receive(JSON.stringify(message));
  }

  it('follows the telemetry frames and clears when no parameter is modulated any more', () => {
    TestBed.configureTestingModule({ providers: [{ provide: ConnectionService, useValue: {} }] });
    const stream = TestBed.inject(EventStream);
    const mix = TestBed.runInInjectionContext(() => computed(() => stream.modulatedValue('delay1', 'mix')));

    receive(stream, { type: 'telemetry', modulation: { delay1: { mix: 30 } } });
    expect(mix()).toBe(30);
    receive(stream, { type: 'telemetry', modulation: { delay1: { mix: 31.5 } } });
    expect(mix()).toBe(31.5);
    receive(stream, { type: 'telemetry' });
    expect(mix()).toBeUndefined();
    expect(stream.modulation()).toBeNull();
  });
});
