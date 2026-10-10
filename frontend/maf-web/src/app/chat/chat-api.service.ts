import { Injectable, InjectionToken, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ChatRequest, ChatStreamEvent } from './chat.models';
import { SseParser } from './sse-parser';

/** Base URL of the Agents API. Defaults to same-origin `/api` (proxied by `ng serve` and nginx). */
export const AGENTS_API_URL = new InjectionToken<string>('AGENTS_API_URL', {
  providedIn: 'root',
  factory: () => '/api',
});

export class ChatApiError extends Error {
  constructor(
    message: string,
    readonly status?: number,
  ) {
    super(message);
  }
}

@Injectable({ providedIn: 'root' })
export class ChatApiService {
  private readonly baseUrl = inject(AGENTS_API_URL);

  /**
   * Streams the multi-agent workflow's events. Uses fetch + ReadableStream because
   * EventSource cannot send a POST body. Unsubscribing aborts the HTTP request.
   */
  streamChat(request: ChatRequest): Observable<ChatStreamEvent> {
    return new Observable<ChatStreamEvent>((subscriber) => {
      const controller = new AbortController();

      (async () => {
        const response = await fetch(`${this.baseUrl}/chat/stream`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', Accept: 'text/event-stream' },
          body: JSON.stringify(request),
          signal: controller.signal,
        });

        if (!response.ok || !response.body) {
          throw new ChatApiError(await describeError(response), response.status);
        }

        const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
        const parser = new SseParser();
        for (;;) {
          const { value, done } = await reader.read();
          if (done) {
            break;
          }
          for (const message of parser.push(value)) {
            subscriber.next(JSON.parse(message.data) as ChatStreamEvent);
          }
        }
        subscriber.complete();
      })().catch((err: unknown) => {
        if (!controller.signal.aborted) {
          subscriber.error(
            err instanceof ChatApiError
              ? err
              : new ChatApiError('Could not reach the agent service.'),
          );
        }
      });

      return () => controller.abort();
    });
  }
}

async function describeError(response: Response): Promise<string> {
  if (response.status === 429) {
    return 'Too many requests — please wait a moment and try again.';
  }
  if (response.status === 400) {
    try {
      const problem = (await response.json()) as {
        title?: string;
        errors?: Record<string, string[]>;
      };
      const details = Object.values(problem.errors ?? {})
        .flat()
        .join(' ');
      return details || problem.title || 'The request was invalid.';
    } catch {
      return 'The request was invalid.';
    }
  }
  return `The agent service returned an error (${response.status}).`;
}
