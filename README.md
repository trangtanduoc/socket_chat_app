```mermaid
graph TD
    Guest["Guest"]
    Customer["Customer"]
    Doctor["Doctor"]
    Manager["Manager"]
    Admin["Admin"]
    PaymentGateway["Payment Gateway"]
    NotificationService["Notification Service"]
    AuthProvider["Authentication Provider"]

    System["Infertility Treatment Management & Monitoring System (IFMMS)"]

    Guest -->|Search Treatments, Sign Up| System
    System -->|Treatment Plans, Info| Guest

    Customer <--> |Appointments, Payments, Treatment Info| System
    Doctor <--> |View/Add Treatment Notes| System
    Manager <--> |Oversee Operations, Reports| System
    Admin <--> |Manage Users, Settings| System

    System -->|Payment Requests| PaymentGateway
    PaymentGateway -->|Payment Status| System

    System -->|Triggers| NotificationService
    NotificationService -->|Reminders via Email/SMS| Customer
    NotificationService -->|Reminders via Email/SMS| Doctor

    System <--> |Credentials, Session Token| AuthProvider
