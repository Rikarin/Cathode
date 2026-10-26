import { failure } from './startup';

describe('startup failure', () => {
  it('reads without the wrapping of an error from the desktop app', () => {
    expect(
      failure(new Error("Error invoking remote method 'cathode:connection': Error: No answer from the Cathode server."))
    ).toBe('No answer from the Cathode server.');
    expect(failure(new Error('No answer from http://127.0.0.1:5180.'))).toBe('No answer from http://127.0.0.1:5180.');
    expect(failure('offline')).toBe('offline');
  });
});
