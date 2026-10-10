import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ChatComponent } from './chat/chat.component';
import { ChatStore } from './chat/chat.store';

@Component({
  selector: 'app-root',
  imports: [ChatComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="topbar">
      <div class="topbar__brand">
        <span class="logo" aria-hidden="true">◆</span>
        <h1>MAF Knowledge Assistant</h1>
      </div>
      <button
        type="button"
        class="btn btn--ghost"
        (click)="store.clear()"
        [disabled]="store.isEmpty()"
      >
        New chat
      </button>
    </header>
    <main class="main">
      <app-chat />
    </main>
  `,
})
export class App {
  protected readonly store = inject(ChatStore);
}
