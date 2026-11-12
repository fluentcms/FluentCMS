// The filter tree of spec 4.4, as FluentCMS.Client and the SDK have it.
export type FilterOperator =
  | "eq"
  | "ne"
  | "in"
  | "nin"
  | "lt"
  | "lte"
  | "gt"
  | "gte"
  | "between"
  | "contains"
  | "startsWith"
  | "null"
  | "notNull"
  | "hasBlock";

export type FilterValue = string | number | bigint | boolean | Date;

export interface FieldFilter {
  readonly kind: "field";
  readonly path: string;
  readonly operator: FilterOperator;
  readonly value?: FilterValue | readonly FilterValue[];
}

export interface AndFilter {
  readonly kind: "and";
  readonly filters: readonly Filter[];
}

export interface OrFilter {
  readonly kind: "or";
  readonly filters: readonly Filter[];
}

export interface NotFilter {
  readonly kind: "not";
  readonly filter: Filter;
}

export type Filter = FieldFilter | AndFilter | OrFilter | NotFilter;

export interface SortField {
  readonly path: string;
  readonly descending: boolean;
}

export function and(...filters: Filter[]): AndFilter {
  return { kind: "and", filters };
}

export function or(...filters: Filter[]): OrFilter {
  return { kind: "or", filters };
}

export function not(filter: Filter): NotFilter {
  return { kind: "not", filter };
}
