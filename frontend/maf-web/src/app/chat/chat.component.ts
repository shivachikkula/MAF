import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { MarkdownPipe } from './markdown.pipe';
import { ANSWER_AGENT, ChatStore } from './chat.store';

@Component({
  selector: 'app-chat',
  imports: [MarkdownPipe],
  templateUrl: './chat.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChatComponent {
  protected readonly store = inject(ChatStore);
  protected readonly answerAgent = inject(ANSWER_AGENT);
  protected readonly draft = signal('');
  protected readonly suggestions = [
    'What documents are in the knowledge base?',
    'Summarise our remote work policy.',
    'How are travel expenses reimbursed?',
  ];

  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');

  constructor() {
    // Keep the newest content in view while the agents stream.
    effect(() => {
      this.store.messages();
      const el = this.scroller()?.nativeElement;
      if (el) {
        queueMicrotask(() => (el.scrollTop = el.scrollHeight));
      }
    });
  }

  protected submit(text = this.draft()): void {
    if (!text.trim() || this.store.isStreaming()) {
      return;
    }
    this.store.send(text);
    this.draft.set('');
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      this.submit();
    }
  }

  protected onInput(event: Event): void {
    const textarea = event.target as HTMLTextAreaElement;
    this.draft.set(textarea.value);
    textarea.style.height = 'auto';
    textarea.style.height = `${Math.min(textarea.scrollHeight, 200)}px`;
  }
}
