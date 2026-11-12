import type { FieldFilter, FilterOperator, FilterValue, SortField } from "./filters.js";

// The field types the generated builders return, with the members of FluentCMS.Client's types in camel case, so that
// ArticleFields.title.contains("x") gives the filter ArticleFields.Title.Contains("x") gives in C#.
export abstract class QueryField {
  readonly path: string;

  constructor(path: string) {
    if (!path) {
      throw new TypeError("A field has a path.");
    }

    this.path = path;
  }

  toString(): string {
    return this.path;
  }
}

// Functions of the module rather than members, so that a generated builder may name a block type filter or sort.
function condition(path: string, operator: FilterOperator, value?: FilterValue | readonly FilterValue[]): FieldFilter {
  return value === undefined ? { kind: "field", path, operator } : { kind: "field", path, operator, value };
}

function order(path: string, descending: boolean): SortField {
  return { path, descending };
}

export type DateValue = string | Date;

export type NumberValue = number | bigint;

export class TextField extends QueryField {
  eq(value: string): FieldFilter { return condition(this.path, "eq", value); }
  ne(value: string): FieldFilter { return condition(this.path, "ne", value); }
  in(...values: string[]): FieldFilter { return condition(this.path, "in", values); }
  notIn(...values: string[]): FieldFilter { return condition(this.path, "nin", values); }
  contains(value: string): FieldFilter { return condition(this.path, "contains", value); }
  startsWith(value: string): FieldFilter { return condition(this.path, "startsWith", value); }
  isNull(): FieldFilter { return condition(this.path, "null"); }
  isNotNull(): FieldFilter { return condition(this.path, "notNull"); }
  ascending(): SortField { return order(this.path, false); }
  descending(): SortField { return order(this.path, true); }
}

export class LongTextField extends QueryField {
  contains(value: string): FieldFilter { return condition(this.path, "contains", value); }
  startsWith(value: string): FieldFilter { return condition(this.path, "startsWith", value); }
  isNull(): FieldFilter { return condition(this.path, "null"); }
  isNotNull(): FieldFilter { return condition(this.path, "notNull"); }
}

export class RichTextField extends QueryField {
  isNull(): FieldFilter { return condition(this.path, "null"); }
  isNotNull(): FieldFilter { return condition(this.path, "notNull"); }
}

// integer and decimal.
export class NumberField extends QueryField {
  eq(value: NumberValue): FieldFilter { return condition(this.path, "eq", value); }
  ne(value: NumberValue): FieldFilter { return condition(this.path, "ne", value); }
  in(...values: NumberValue[]): FieldFilter { return condition(this.path, "in", values); }
  notIn(...values: NumberValue[]): FieldFilter { return condition(this.path, "nin", values); }
  lt(value: NumberValue): FieldFilter { return condition(this.path, "lt", value); }
  lte(value: NumberValue): FieldFilter { return condition(this.path, "lte", value); }
  gt(value: NumberValue): FieldFilter { return condition(this.path, "gt", value); }
  gte(value: NumberValue): FieldFilter { return condition(this.path, "gte", value); }
  between(from: NumberValue, to: NumberValue): FieldFilter { return condition(this.path, "between", [from, to]); }
  isNull(): FieldFilter { return condition(this.path, "null"); }
  isNotNull(): FieldFilter { return condition(this.path, "notNull"); }
  ascending(): SortField { return order(this.path, false); }
  descending(): SortField { return order(this.path, true); }
}

// date (yyyy-MM-dd) and dateTime (a Date, or an ISO 8601 text with an offset).
export class DateField extends QueryField {
  eq(value: DateValue): FieldFilter { return condition(this.path, "eq", value); }
  ne(value: DateValue): FieldFilter { return condition(this.path, "ne", value); }
  in(...values: DateValue[]): FieldFilter { return condition(this.path, "in", values); }
  notIn(...values: DateValue[]): FieldFilter { return condition(this.path, "nin", values); }
  lt(value: DateValue): FieldFilter { return condition(this.path, "lt", value); }
  lte(value: DateValue): FieldFilter { return condition(this.path, "lte", value); }
  gt(value: DateValue): FieldFilter { return condition(this.path, "gt", value); }
  gte(value: DateValue): FieldFilter { return condition(this.path, "gte", value); }
  between(from: DateValue, to: DateValue): FieldFilter { return condition(this.path, "between", [from, to]); }
  isNull(): FieldFilter { return condition(this.path, "null"); }
  isNotNull(): FieldFilter { return condition(this.path, "notNull"); }
  ascending(): SortField { return order(this.path, false); }
  descending(): SortField { return order(this.path, true); }
}

export class BooleanField extends QueryField {
  eq(value: boolean): FieldFilter { return condition(this.path, "eq", value); }
  ne(value: boolean): FieldFilter { return condition(this.path, "ne", value); }
  isNull(): FieldFilter { return condition(this.path, "null"); }
  isNotNull(): FieldFilter { return condition(this.path, "notNull"); }
  ascending(): SortField { return order(this.path, false); }
  descending(): SortField { return order(this.path, true); }
}

export class SelectField extends QueryField {
  eq(option: string): FieldFilter { return condition(this.path, "eq", option); }
  ne(option: string): FieldFilter { return condition(this.path, "ne", option); }
  in(...options: string[]): FieldFilter { return condition(this.path, "in", options); }
  notIn(...options: string[]): FieldFilter { return condition(this.path, "nin", options); }
  contains(value: string): FieldFilter { return condition(this.path, "contains", value); }
  isNull(): FieldFilter { return condition(this.path, "null"); }
  isNotNull(): FieldFilter { return condition(this.path, "notNull"); }
  ascending(): SortField { return order(this.path, false); }
  descending(): SortField { return order(this.path, true); }
}

export class SelectListField extends QueryField {
  contains(option: string): FieldFilter { return condition(this.path, "contains", option); }
  in(...options: string[]): FieldFilter { return condition(this.path, "in", options); }
  notIn(...options: string[]): FieldFilter { return condition(this.path, "nin", options); }
  isNull(): FieldFilter { return condition(this.path, "null"); }
  isNotNull(): FieldFilter { return condition(this.path, "notNull"); }
}

export class RelationField extends QueryField {
  eq(id: string): FieldFilter { return condition(this.path, "eq", id); }
  in(...ids: string[]): FieldFilter { return condition(this.path, "in", ids); }
  isNull(): FieldFilter { return condition(this.path, "null"); }
  isNotNull(): FieldFilter { return condition(this.path, "notNull"); }
}

export class MediaField extends QueryField {
  eq(assetId: string): FieldFilter { return condition(this.path, "eq", assetId); }
  in(...assetIds: string[]): FieldFilter { return condition(this.path, "in", assetIds); }
}

export class JsonField extends QueryField {
  key(name: string): JsonKeyField {
    if (!/^[A-Za-z0-9_]+$/.test(name)) {
      throw new TypeError("A filter reaches one key inside JSON, named with ASCII letters, digits and underscores.");
    }

    return new JsonKeyField(`${this.path}.${name}`);
  }

  isNull(): FieldFilter { return condition(this.path, "null"); }
  isNotNull(): FieldFilter { return condition(this.path, "notNull"); }
}

export class JsonKeyField extends QueryField {
  eq(value: string | NumberValue | boolean): FieldFilter { return condition(this.path, "eq", value); }
  ne(value: string | NumberValue | boolean): FieldFilter { return condition(this.path, "ne", value); }
  contains(value: string): FieldFilter { return condition(this.path, "contains", value); }
}

// The generated builder of a blocks field derives from it, with a member per block type of the field.
export class BlocksField extends QueryField {
  hasBlock(blockType: string): FieldFilter {
    if (!blockType) {
      throw new TypeError("hasBlock takes a block type.");
    }

    return condition(this.path, "hasBlock", blockType);
  }

  isNull(): FieldFilter { return condition(this.path, "null"); }
  isNotNull(): FieldFilter { return condition(this.path, "notNull"); }
}

export class BlockTextField extends QueryField {
  eq(value: string): FieldFilter { return condition(this.path, "eq", value); }
  ne(value: string): FieldFilter { return condition(this.path, "ne", value); }
  contains(value: string): FieldFilter { return condition(this.path, "contains", value); }
}

// A number, a boolean or a date one level inside a block.
export class BlockValueField extends QueryField {
  eq(value: NumberValue | boolean | DateValue): FieldFilter { return condition(this.path, "eq", value); }
  ne(value: NumberValue | boolean | DateValue): FieldFilter { return condition(this.path, "ne", value); }
}

export class BlockContainsField extends QueryField {
  contains(value: string): FieldFilter { return condition(this.path, "contains", value); }
}
