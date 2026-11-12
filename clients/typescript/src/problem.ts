export interface ProblemError {
  path?: string;
  code?: string;
  message?: string;
}

// A problem (RFC 9457) the API answered, with its FCMS code. A response that is not a problem has no code.
export class FluentCmsProblem extends Error {
  readonly status: number;
  readonly code: string | undefined;
  readonly title: string | undefined;
  readonly detail: string | undefined;
  readonly errors: readonly ProblemError[];
  readonly traceId: string | undefined;

  constructor(status: number, body: unknown) {
    const problem = typeof body === "object" && body !== null ? (body as Record<string, unknown>) : {};
    const code = text(problem["code"]);
    const title = text(problem["title"]);
    const detail = text(problem["detail"]);
    super(`${code ?? status}: ${detail ?? title ?? "the request failed"}`);
    this.name = "FluentCmsProblem";
    this.status = status;
    this.code = code;
    this.title = title;
    this.detail = detail;
    this.traceId = text(problem["traceId"]);
    this.errors = Array.isArray(problem["errors"])
      ? problem["errors"].filter((item): item is Record<string, unknown> => typeof item === "object" && item !== null).map((item) => error(item))
      : [];
  }
}

function error(item: Record<string, unknown>): ProblemError {
  const result: ProblemError = {};
  const path = text(item["path"]);
  const code = text(item["code"]);
  const message = text(item["message"]);
  if (path !== undefined) result.path = path;
  if (code !== undefined) result.code = code;
  if (message !== undefined) result.message = message;
  return result;
}

function text(value: unknown): string | undefined {
  return typeof value === "string" ? value : undefined;
}
