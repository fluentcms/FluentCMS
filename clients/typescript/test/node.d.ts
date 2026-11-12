// The parts of Node's own modules the tests use, so that the package needs no type package beside typescript.
declare module "node:test" {
  export function test(name: string, run: () => void | Promise<void>): Promise<void>;
}

declare module "node:assert/strict" {
  interface Assert {
    equal(actual: unknown, expected: unknown, message?: string): void;
    deepEqual(actual: unknown, expected: unknown, message?: string): void;
    ok(value: unknown, message?: string): void;
    throws(run: () => unknown, check?: (error: unknown) => boolean): void;
    rejects(run: Promise<unknown> | (() => Promise<unknown>), check?: (error: unknown) => boolean): Promise<void>;
  }

  const assert: Assert;
  export default assert;
}

declare module "node:fs" {
  export function readFileSync(path: URL, encoding: "utf8"): string;
}
