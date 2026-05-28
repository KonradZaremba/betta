# Betta Project Rules & Architecture Principles

## Core Principles

### 1. Service-First Design
- Services define the business logic, components are just wrappers
- Services should be framework-agnostic (no Grasshopper dependencies)
- Use dependency injection for service registration and resolution

### 2. Zero-Touch Component Generation
- Developers decorate service methods with attributes
- Components are generated automatically at assembly load time
- No manual component creation required

### 3. Type Safety & Performance
- Strong typing throughout the pipeline
- Efficient type conversion without boxing/unboxing when possible
- Proper handling of Grasshopper data structures (items/lists/trees)

### 4. .NET Framework 4.8 Compatibility
- All code must be compatible with .NET Framework 4.8
- Use Microsoft.Extensions.DependencyInjection for DI container
- Avoid newer C# language features that aren't supported

## Code Standards

### 1. Naming Conventions
- Services: `IServiceName` / `ServiceName`
- Components: Auto-generated names from service methods
- Attributes: `GrasshopperXxxAttribute`

### 2. Error Handling
- Always wrap service method calls in try-catch
- Use Grasshopper's AddRuntimeMessage for user feedback
- Log errors for debugging

### 3. Testing
- Unit tests for all core functionality
- Integration tests with actual Grasshopper documents
- Test coverage > 80%

### 4. Performance
- Lazy initialization where possible
- Cache reflection results
- Minimize allocations in solve loops

## Forbidden Patterns

? **Don't create components manually in PriorityLoad**
? **Don't use static state for component-specific data**
? **Don't reference Grasshopper types in services**
? **Don't ignore type conversion errors**
? **Don't use string-based type checking**

## Required Patterns

? **Use attributes for component metadata**
? **Use DI container for service resolution**
? **Use proper type conversion with reflection**
? **Use DA.SetData/SetDataList for outputs**
? **Use instance-level component state**