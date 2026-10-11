/** Wire format of a Server-Sent Event emitted by the Agents API (`POST /api/chat/stream`). */
export type ChatStreamEventType =
  'agent_started' | 'delta' | 'tool_call' | 'tool_result' | 'final' | 'error';

export interface ChatStreamEvent {
  type: ChatStreamEventType;
  agent?: string;
  text?: string;
  tool?: string;
  arguments?: string;
}

export interface ChatHistoryMessage {
  role: 'user' | 'assistant';
  content: string;
}

export interface ChatRequest {
  message: string;
  history: ChatHistoryMessage[];
}

export interface ToolCallView {
  tool: string;
  args?: string;
  done: boolean;
}

/** One agent's contribution to an answer (shown in the "agent activity" panel). */
export interface AgentStepView {
  agent: string;
  text: string;
  toolCalls: ToolCallView[];
}

export type MessageStatus = 'streaming' | 'done' | 'error' | 'cancelled';

export interface ChatMessageView {
  id: number;
  role: 'user' | 'assistant';
  content: string;
  steps: AgentStepView[];
  status: MessageStatus;
  error?: string;
}
