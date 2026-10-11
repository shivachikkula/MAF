import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { ChatApiService } from './chat-api.service';
import { ChatRequest, ChatStreamEvent } from './chat.models';
import { ChatStore } from './chat.store';

describe('ChatStore', () => {
  let stream: Subject<ChatStreamEvent>;
  let requests: ChatRequest[];
  let store: ChatStore;

  beforeEach(() => {
    requests = [];
    TestBed.configureTestingModule({
      providers: [
        {
          provide: ChatApiService,
          useValue: {
            streamChat: (r: ChatRequest) => {
              requests.push(r);
              stream = new Subject<ChatStreamEvent>();
              return stream.asObservable();
            },
          },
        },
      ],
    });
    store = TestBed.inject(ChatStore);
  });

  function runHappyPath(): void {
    store.send('How many vacation days?');
    stream.next({ type: 'agent_started', agent: 'Researcher' });
    stream.next({
      type: 'tool_call',
      agent: 'Researcher',
      tool: 'search_knowledge_base',
      arguments: '{}',
    });
    stream.next({ type: 'tool_result', agent: 'Researcher', tool: 'call_1' });
    stream.next({ type: 'delta', agent: 'Researcher', text: 'RESEARCH NOTES' });
    stream.next({ type: 'agent_started', agent: 'Writer' });
    stream.next({ type: 'delta', agent: 'Writer', text: '25 ' });
    stream.next({ type: 'delta', agent: 'Writer', text: 'days.' });
  }

  it('builds agent steps and streams only the answer agent into the content', () => {
    runHappyPath();

    const assistant = store.messages()[1];
    expect(store.isStreaming()).toBe(true);
    expect(assistant.content).toBe('25 days.');
    expect(assistant.steps.map((s) => s.agent)).toEqual(['Researcher', 'Writer']);
    expect(assistant.steps[0].toolCalls).toEqual([
      { tool: 'search_knowledge_base', args: '{}', done: true },
    ]);
    expect(assistant.steps[0].text).toBe('RESEARCH NOTES');

    stream.next({ type: 'final', agent: 'Writer', text: '25 days.' });
    stream.complete();
    expect(store.isStreaming()).toBe(false);
    expect(store.messages()[1].status).toBe('done');
  });

  it('sends completed turns as history', () => {
    runHappyPath();
    stream.next({ type: 'final', text: '25 days.' });
    stream.complete();

    store.send('And part-time?');
    expect(requests[1]).toEqual({
      message: 'And part-time?',
      history: [
        { role: 'user', content: 'How many vacation days?' },
        { role: 'assistant', content: '25 days.' },
      ],
    });
  });

  it('marks the message as failed on error events and transport errors', () => {
    store.send('q1');
    stream.next({ type: 'error', text: 'boom' });
    expect(store.messages()[1]).toMatchObject({ status: 'error', error: 'boom' });

    store.send('q2');
    stream.error(new Error('offline'));
    expect(store.messages()[3]).toMatchObject({ status: 'error', error: 'offline' });
  });

  it('ignores sends while streaming and can be stopped', () => {
    store.send('first');
    store.send('second');
    expect(requests.length).toBe(1);

    store.stop();
    expect(store.messages()[1].status).toBe('cancelled');
    expect(store.isStreaming()).toBe(false);
  });
});
