import { Injectable, InjectionToken, computed, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { ChatApiService } from './chat-api.service';
import { AgentStepView, ChatHistoryMessage, ChatMessageView, ChatStreamEvent } from './chat.models';

/** Name of the agent whose output is the user-facing answer (the last agent in the workflow). */
export const ANSWER_AGENT = new InjectionToken<string>('ANSWER_AGENT', {
  providedIn: 'root',
  factory: () => 'Writer',
});

const MAX_HISTORY = 20;

/** Signal-based state for the conversation and the in-flight agent run. */
@Injectable({ providedIn: 'root' })
export class ChatStore {
  private readonly api = inject(ChatApiService);
  private readonly answerAgent = inject(ANSWER_AGENT);

  private nextId = 1;
  private subscription?: Subscription;

  private readonly _messages = signal<ChatMessageView[]>([]);
  readonly messages = this._messages.asReadonly();
  readonly isStreaming = computed(() => this._messages().some((m) => m.status === 'streaming'));
  readonly isEmpty = computed(() => this._messages().length === 0);

  send(text: string): void {
    const message = text.trim();
    if (!message || this.isStreaming()) {
      return;
    }

    const history = this.buildHistory();
    const assistantId = this.nextId + 1;
    this._messages.update((list) => [
      ...list,
      { id: this.nextId, role: 'user', content: message, steps: [], status: 'done' },
      { id: assistantId, role: 'assistant', content: '', steps: [], status: 'streaming' },
    ]);
    this.nextId += 2;

    this.subscription = this.api.streamChat({ message, history }).subscribe({
      next: (event) => this.apply(assistantId, event),
      error: (err: Error) =>
        this.patch(assistantId, (m) => ({ ...m, status: 'error', error: err.message })),
      complete: () =>
        this.patch(assistantId, (m) =>
          m.status === 'streaming'
            ? { ...m, status: 'error', error: 'The response ended unexpectedly.' }
            : m,
        ),
    });
  }

  stop(): void {
    this.subscription?.unsubscribe();
    this._messages.update((list) =>
      list.map((m) => (m.status === 'streaming' ? { ...m, status: 'cancelled' } : m)),
    );
  }

  clear(): void {
    this.stop();
    this._messages.set([]);
  }

  /** Applies one streamed event to the assistant message (pure state transition). */
  private apply(id: number, event: ChatStreamEvent): void {
    this.patch(id, (m) => {
      switch (event.type) {
        case 'agent_started':
          return {
            ...m,
            steps: [...m.steps, { agent: event.agent ?? 'Agent', text: '', toolCalls: [] }],
          };
        case 'delta': {
          const steps = updateLastStep(m.steps, (s) => ({
            ...s,
            text: s.text + (event.text ?? ''),
          }));
          const content =
            event.agent === this.answerAgent ? m.content + (event.text ?? '') : m.content;
          return { ...m, steps, content };
        }
        case 'tool_call':
          return {
            ...m,
            steps: updateLastStep(m.steps, (s) => ({
              ...s,
              toolCalls: [
                ...s.toolCalls,
                { tool: event.tool ?? 'tool', args: event.arguments, done: false },
              ],
            })),
          };
        case 'tool_result':
          return {
            ...m,
            steps: updateLastStep(m.steps, (s) => {
              const index = s.toolCalls.findIndex((c) => !c.done);
              return index < 0
                ? s
                : {
                    ...s,
                    toolCalls: s.toolCalls.map((c, i) => (i === index ? { ...c, done: true } : c)),
                  };
            }),
          };
        case 'final':
          return { ...m, content: event.text ?? m.content, status: 'done' };
        case 'error':
          return { ...m, status: 'error', error: event.text ?? 'Something went wrong.' };
        default:
          return m;
      }
    });
  }

  private patch(id: number, fn: (m: ChatMessageView) => ChatMessageView): void {
    this._messages.update((list) => list.map((m) => (m.id === id ? fn(m) : m)));
  }

  private buildHistory(): ChatHistoryMessage[] {
    return this._messages()
      .filter((m) => m.status === 'done' && m.content)
      .map((m) => ({ role: m.role, content: m.content }))
      .slice(-MAX_HISTORY);
  }
}

function updateLastStep(
  steps: AgentStepView[],
  fn: (s: AgentStepView) => AgentStepView,
): AgentStepView[] {
  return steps.length ? [...steps.slice(0, -1), fn(steps[steps.length - 1])] : steps;
}
