export interface SseMessage {
  event: string;
  data: string;
}

/**
 * Incremental Server-Sent Events parser (https://html.spec.whatwg.org/#event-stream-interpretation).
 * Feed it decoded text chunks as they arrive; it returns every complete event found so far
 * and buffers any partial event until the next chunk.
 */
export class SseParser {
  private buffer = '';

  push(chunk: string): SseMessage[] {
    this.buffer += chunk.replace(/\r\n?/g, '\n');
    const messages: SseMessage[] = [];

    let boundary: number;
    while ((boundary = this.buffer.indexOf('\n\n')) >= 0) {
      const block = this.buffer.slice(0, boundary);
      this.buffer = this.buffer.slice(boundary + 2);
      const message = SseParser.parseBlock(block);
      if (message) {
        messages.push(message);
      }
    }

    return messages;
  }

  private static parseBlock(block: string): SseMessage | null {
    let event = 'message';
    const data: string[] = [];

    for (const line of block.split('\n')) {
      if (!line || line.startsWith(':')) {
        continue; // blank line or comment / keep-alive
      }
      const colon = line.indexOf(':');
      const field = colon < 0 ? line : line.slice(0, colon);
      let value = colon < 0 ? '' : line.slice(colon + 1);
      if (value.startsWith(' ')) {
        value = value.slice(1);
      }
      if (field === 'event') {
        event = value;
      } else if (field === 'data') {
        data.push(value);
      }
    }

    return data.length ? { event, data: data.join('\n') } : null;
  }
}
