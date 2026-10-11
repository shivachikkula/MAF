import { SseParser } from './sse-parser';

describe('SseParser', () => {
  it('parses complete events with names and data', () => {
    const parser = new SseParser();
    const events = parser.push('event: delta\ndata: {"a":1}\n\nevent: final\ndata: {"b":2}\n\n');
    expect(events).toEqual([
      { event: 'delta', data: '{"a":1}' },
      { event: 'final', data: '{"b":2}' },
    ]);
  });

  it('buffers events split across chunks', () => {
    const parser = new SseParser();
    expect(parser.push('event: del')).toEqual([]);
    expect(parser.push('ta\ndata: {"x"')).toEqual([]);
    expect(parser.push(':true}\n\n')).toEqual([{ event: 'delta', data: '{"x":true}' }]);
  });

  it('handles CRLF, comments and multi-line data', () => {
    const parser = new SseParser();
    const events = parser.push(': keep-alive\r\n\r\ndata: line1\r\ndata: line2\r\n\r\n');
    expect(events).toEqual([{ event: 'message', data: 'line1\nline2' }]);
  });
});
