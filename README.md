```mermaid
graph TD

    Guest["Guest"]
    Customer["Customer"]
    Doctor["Doctor"]
    Manager["Manager"]
    Admin["Admin"]
    PaymentGateway["Payment Gateway"]
    NotificationService["Notification Service"]

    System["Infertility Treatment Management System (IFMMS)"]

    Guest -->|Views Services, Registers| System
    System -->|Treatment Info, Blogs| Guest

    Customer -->|Register, Book Appointments, Feedback| System
    System -->|Treatment Updates, Notifications| Customer

    Doctor -->|View/Update Treatment, Notes| System
    System -->|Patient Data, Schedule| Doctor

    Manager -->|Manage Appointments, Payments| System
    System -->|Treatment Reports| Manager

    Admin -->|Manage Users, Reports| System
    System -->|System Status, Data| Admin

    System -->|Send Reminders| NotificationService
    NotificationService -->|Email/SMS| Customer
    NotificationService -->|Email/SMS| Doctor

    System -->|Payment Request| PaymentGateway
    PaymentGateway -->|Payment Status| System
