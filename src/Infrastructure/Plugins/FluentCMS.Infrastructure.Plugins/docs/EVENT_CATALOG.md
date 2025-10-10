# Event Catalog

This document serves as a central registry for all domain events shared between plugins. Maintaining this catalog is crucial for understanding the implicit, event-driven dependencies within the system.

**Policy**: Events, once published and consumed by other plugins, should be considered immutable. To introduce a breaking change, create a new version of the event (e.g., `CustomerCreatedEventV2`) and deprecate the old one. See the "Plugin Governance" section in the Plugin Development Guide for more details.

---

## Template

*   **Event Name**: `ExampleEvent`
*   **Contracts Project**: The project where the event class is defined.
*   **Publishing Plugin(s)**: The plugin(s) known to publish this event.
*   **Known Subscribing Plugin(s)**: The plugin(s) known to handle this event.
*   **Description**: A brief explanation of when and why this event is published.
*   **Data Payload**: A description of the properties included in the event.

| Property | Type | Description |
|----------|------|-------------|
| `ExampleId` | `Guid` | The unique identifier for the example entity. |
| `ExampleValue` | `string` | A sample value associated with the event. |

---
*(This catalog should be populated as events are created).*
