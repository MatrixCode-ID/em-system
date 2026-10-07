namespace Em.Shared
{
   /// <summary>
   /// Bundles several objects into one POST payload, for actions that need more than one object
   /// parameter. The <c>Build</c> overloads infer the generic types.
   /// </summary>
   public abstract class DtoPayload
   {
      /// <summary>Creates a payload with one object.</summary>
      public static DtoPayload<T1> Build<T1>(T1 payload1) => new(payload1);

      /// <summary>Creates a payload with two objects.</summary>
      public static DtoPayload<T1, T2> Build<T1, T2>(T1 payload1, T2 payload2) => new(payload1, payload2);

      /// <summary>Creates a payload with three objects.</summary>
      public static DtoPayload<T1, T2, T3> Build<T1, T2, T3>(T1 payload1, T2 payload2, T3 payload3) =>
         new(payload1, payload2, payload3);

      /// <summary>Creates a payload with four objects.</summary>
      public static DtoPayload<T1, T2, T3, T4> Build<T1, T2, T3, T4>(T1 payload1, T2 payload2, T3 payload3,
         T4 payload4) =>
         new(payload1, payload2, payload3, payload4);

      /// <summary>Creates a payload with five objects.</summary>
      public static DtoPayload<T1, T2, T3, T4, T5> Build<T1, T2, T3, T4, T5>(T1 payload1, T2 payload2, T3 payload3,
         T4 payload4, T5 payload5) =>
         new(payload1, payload2, payload3, payload4, payload5);

      /// <summary>Creates a payload with six objects.</summary>
      public static DtoPayload<T1, T2, T3, T4, T5, T6> Build<T1, T2, T3, T4, T5, T6>(T1 payload1, T2 payload2,
         T3 payload3, T4 payload4, T5 payload5, T6 payload6) =>
         new(payload1, payload2, payload3, payload4, payload5, payload6);

      /// <summary>Creates a payload with seven objects.</summary>
      public static DtoPayload<T1, T2, T3, T4, T5, T6, T7> Build<T1, T2, T3, T4, T5, T6, T7>(T1 payload1,
         T2 payload2, T3 payload3, T4 payload4, T5 payload5, T6 payload6, T7 payload7) =>
         new(payload1, payload2, payload3, payload4, payload5, payload6, payload7);

      /// <summary>Creates a payload with eight objects.</summary>
      public static DtoPayload<T1, T2, T3, T4, T5, T6, T7, T8> Build<T1, T2, T3, T4, T5, T6, T7, T8>(T1 payload1,
         T2 payload2, T3 payload3, T4 payload4, T5 payload5, T6 payload6, T7 payload7, T8 payload8) =>
         new(payload1, payload2, payload3, payload4, payload5, payload6, payload7, payload8);

      /// <summary>Creates a payload with nine objects.</summary>
      public static DtoPayload<T1, T2, T3, T4, T5, T6, T7, T8, T9> Build<T1, T2, T3, T4, T5, T6, T7, T8, T9>(
         T1 payload1, T2 payload2, T3 payload3, T4 payload4, T5 payload5, T6 payload6, T7 payload7, T8 payload8,
         T9 payload9) =>
         new(payload1, payload2, payload3, payload4, payload5, payload6, payload7, payload8, payload9);

      /// <summary>Creates a payload with ten objects.</summary>
      public static DtoPayload<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> Build<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
         T1 payload1, T2 payload2, T3 payload3, T4 payload4, T5 payload5, T6 payload6, T7 payload7, T8 payload8,
         T9 payload9, T10 payload10) =>
         new(payload1, payload2, payload3, payload4, payload5, payload6, payload7, payload8, payload9, payload10);
   }

   // The constructor parameters are named after the properties they fill on purpose. These types carry
   // no parameterless constructor, so System.Text.Json has to deserialize them through the single public
   // constructor, and it only binds a constructor parameter to a property whose name matches it (ignoring
   // case). A parameter named anything else makes the whole type undeserializable at runtime - the
   // serializer throws instead of falling back - which would break every POST action taking a DtoPayload.
   /// <summary>Payload with one object.</summary>
   public class DtoPayload<T1>(T1 payload1) : DtoPayload
   {
      /// <summary>First object.</summary>
      public T1 Payload1 { get; init; } = payload1;
   }

   /// <summary>Payload with two objects.</summary>
   public class DtoPayload<T1, T2>(T1 payload1, T2 payload2) : DtoPayload<T1>(payload1)
   {
      /// <summary>Second object.</summary>
      public T2 Payload2 { get; init; } = payload2;
   }

   /// <summary>Payload with three objects.</summary>
   public class DtoPayload<T1, T2, T3>(T1 payload1, T2 payload2, T3 payload3) : DtoPayload<T1, T2>(payload1, payload2)
   {
      /// <summary>Third object.</summary>
      public T3 Payload3 { get; init; } = payload3;
   }

   /// <summary>Payload with four objects.</summary>
   public class DtoPayload<T1, T2, T3, T4>(T1 payload1, T2 payload2, T3 payload3, T4 payload4)
      : DtoPayload<T1, T2, T3>(payload1, payload2, payload3)
   {
      /// <summary>Fourth object.</summary>
      public T4 Payload4 { get; init; } = payload4;
   }

   /// <summary>Payload with five objects.</summary>
   public class DtoPayload<T1, T2, T3, T4, T5>(T1 payload1, T2 payload2, T3 payload3, T4 payload4, T5 payload5)
      : DtoPayload<T1, T2, T3, T4>(payload1, payload2, payload3, payload4)
   {
      /// <summary>Fifth object.</summary>
      public T5 Payload5 { get; init; } = payload5;
   }

   /// <summary>Payload with six objects.</summary>
   public class DtoPayload<T1, T2, T3, T4, T5, T6>(T1 payload1, T2 payload2, T3 payload3, T4 payload4, T5 payload5,
      T6 payload6)
      : DtoPayload<T1, T2, T3, T4, T5>(payload1, payload2, payload3, payload4, payload5)
   {
      /// <summary>Sixth object.</summary>
      public T6 Payload6 { get; init; } = payload6;
   }

   /// <summary>Payload with seven objects.</summary>
   public class DtoPayload<T1, T2, T3, T4, T5, T6, T7>(T1 payload1, T2 payload2, T3 payload3, T4 payload4,
      T5 payload5, T6 payload6, T7 payload7)
      : DtoPayload<T1, T2, T3, T4, T5, T6>(payload1, payload2, payload3, payload4, payload5, payload6)
   {
      /// <summary>Seventh object.</summary>
      public T7 Payload7 { get; init; } = payload7;
   }

   /// <summary>Payload with eight objects.</summary>
   public class DtoPayload<T1, T2, T3, T4, T5, T6, T7, T8>(T1 payload1, T2 payload2, T3 payload3, T4 payload4,
      T5 payload5, T6 payload6, T7 payload7, T8 payload8)
      : DtoPayload<T1, T2, T3, T4, T5, T6, T7>(payload1, payload2, payload3, payload4, payload5, payload6, payload7)
   {
      /// <summary>Eighth object.</summary>
      public T8 Payload8 { get; init; } = payload8;
   }

   /// <summary>Payload with nine objects.</summary>
   public class DtoPayload<T1, T2, T3, T4, T5, T6, T7, T8, T9>(T1 payload1, T2 payload2, T3 payload3, T4 payload4,
      T5 payload5, T6 payload6, T7 payload7, T8 payload8, T9 payload9)
      : DtoPayload<T1, T2, T3, T4, T5, T6, T7, T8>(payload1, payload2, payload3, payload4, payload5, payload6,
         payload7, payload8)
   {
      /// <summary>Ninth object.</summary>
      public T9 Payload9 { get; init; } = payload9;
   }

   /// <summary>Payload with ten objects.</summary>
   public class DtoPayload<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(T1 payload1, T2 payload2, T3 payload3,
      T4 payload4, T5 payload5, T6 payload6, T7 payload7, T8 payload8, T9 payload9, T10 payload10)
      : DtoPayload<T1, T2, T3, T4, T5, T6, T7, T8, T9>(payload1, payload2, payload3, payload4, payload5, payload6,
         payload7, payload8, payload9)
   {
      /// <summary>Tenth object.</summary>
      public T10 Payload10 { get; init; } = payload10;
   }
}
