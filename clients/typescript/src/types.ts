// The system fields of an entry, which the server sets; a write leaves them out.
export interface Entry {
  id: string;
  status?: string;
  locale?: string;
  version?: number;
  createdAt?: string;
  createdBy?: string;
  updatedAt?: string;
  updatedBy?: string;
  publishedAt?: string | null;
}

export interface Reference<TType extends string = string> {
  id: string;
  type: TType;
}

// A relation as a read gives it: a reference, or the populated entry. A write sends the id.
export type Relation<TEntry, TType extends string = string> = Reference<TType> | (TEntry & Reference<TType>);

// A media field as a read gives it: a reference { id, type: "media" }, or the populated asset. A write sends the id.
export interface MediaReference extends Reference<"media"> {
  fileName?: string;
  mimeType?: string;
  size?: number;
  width?: number | null;
  height?: number | null;
  title?: string | null;
  alt?: string | null;
  caption?: string | null;
  credit?: string | null;
  focalPoint?: { x: number; y: number } | null;
  urls?: { original?: string; presets?: Record<string, string>; template?: string } | null;
}

export interface RichTextMark {
  type: string;
  attrs?: Record<string, unknown>;
}

export interface RichTextNode {
  type: string;
  attrs?: Record<string, unknown>;
  content?: RichTextNode[];
  marks?: RichTextMark[];
  text?: string;
}

// A ProseMirror document, or the HTML or Markdown that richText=html or markdown asks for.
export type RichText = (RichTextNode & { type: "doc" }) | string;

export interface EntryPage<T> {
  items: T[];
  total?: number | null;
  page?: number | null;
  pageSize?: number | null;
  nextCursor?: string | null;
}

export interface PublishWarning {
  path: string;
  code: string;
  message: string;
  targetType?: string | null;
  targetId?: string | null;
}

export interface PublishResult<T> {
  entry: T;
  warnings: PublishWarning[];
}

// What a write sends for an entry: its fields, with a relation or a media field as an id.
export type Input<T> = { [K in Exclude<keyof T, keyof Entry | "fallbacks">]?: InputValue<T[K]> };

export type InputValue<V> = V extends Reference<string>
  ? string
  : V extends readonly (infer Item)[]
    ? InputValue<Item>[]
    : V extends object
      ? { [K in keyof V]: InputValue<V[K]> }
      : V;
