namespace Em.Shared
{
   public abstract class DtoPayload
   {
      public static DtoPayload<T1> Build<T1>(T1 payload1) => new(payload1);

      public static DtoPayload<T1, T2> Build<T1, T2>(T1 payload1, T2 payload2) => new(payload1, payload2);

      public static DtoPayload<T1, T2, T3> Build<T1, T2, T3>(T1 payload1, T2 payload2, T3 payload3) =>
         new(payload1, payload2, payload3);

      public static DtoPayload<T1, T2, T3, T4> Build<T1, T2, T3, T4>(T1 payload1, T2 payload2, T3 payload3,
         T4 payload4) =>
         new(payload1, payload2, payload3, payload4);

      public static DtoPayload<T1, T2, T3, T4, T5> Build<T1, T2, T3, T4, T5>(T1 payload1, T2 payload2, T3 payload3,
         T4 payload4, T5 payload5) =>
         new(payload1, payload2, payload3, payload4, payload5);

      public static DtoPayload<T1, T2, T3, T4, T5, T6> Build<T1, T2, T3, T4, T5, T6>(T1 payload1, T2 payload2,
         T3 payload3, T4 payload4, T5 payload5, T6 payload6) =>
         new(payload1, payload2, payload3, payload4, payload5, payload6);

      public static DtoPayload<T1, T2, T3, T4, T5, T6, T7> Build<T1, T2, T3, T4, T5, T6, T7>(T1 payload1,
         T2 payload2, T3 payload3, T4 payload4, T5 payload5, T6 payload6, T7 payload7) =>
         new(payload1, payload2, payload3, payload4, payload5, payload6, payload7);

      public static DtoPayload<T1, T2, T3, T4, T5, T6, T7, T8> Build<T1, T2, T3, T4, T5, T6, T7, T8>(T1 payload1,
         T2 payload2, T3 payload3, T4 payload4, T5 payload5, T6 payload6, T7 payload7, T8 payload8) =>
         new(payload1, payload2, payload3, payload4, payload5, payload6, payload7, payload8);

      public static DtoPayload<T1, T2, T3, T4, T5, T6, T7, T8, T9> Build<T1, T2, T3, T4, T5, T6, T7, T8, T9>(
         T1 payload1, T2 payload2, T3 payload3, T4 payload4, T5 payload5, T6 payload6, T7 payload7, T8 payload8,
         T9 payload9) =>
         new(payload1, payload2, payload3, payload4, payload5, payload6, payload7, payload8, payload9);

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
   public class DtoPayload<T1>(T1 payload1) : DtoPayload
   {
      public T1 Payload1 { get; init; } = payload1;
   }

   public class DtoPayload<T1, T2>(T1 payload1, T2 payload2) : DtoPayload<T1>(payload1)
   {
      public T2 Payload2 { get; init; } = payload2;
   }

   public class DtoPayload<T1, T2, T3>(T1 payload1, T2 payload2, T3 payload3) : DtoPayload<T1, T2>(payload1, payload2)
   {
      public T3 Payload3 { get; init; } = payload3;
   }

   public class DtoPayload<T1, T2, T3, T4>(T1 payload1, T2 payload2, T3 payload3, T4 payload4)
      : DtoPayload<T1, T2, T3>(payload1, payload2, payload3)
   {
      public T4 Payload4 { get; init; } = payload4;
   }

   public class DtoPayload<T1, T2, T3, T4, T5>(T1 payload1, T2 payload2, T3 payload3, T4 payload4, T5 payload5)
      : DtoPayload<T1, T2, T3, T4>(payload1, payload2, payload3, payload4)
   {
      public T5 Payload5 { get; init; } = payload5;
   }

   public class DtoPayload<T1, T2, T3, T4, T5, T6>(T1 payload1, T2 payload2, T3 payload3, T4 payload4, T5 payload5,
      T6 payload6)
      : DtoPayload<T1, T2, T3, T4, T5>(payload1, payload2, payload3, payload4, payload5)
   {
      public T6 Payload6 { get; init; } = payload6;
   }

   public class DtoPayload<T1, T2, T3, T4, T5, T6, T7>(T1 payload1, T2 payload2, T3 payload3, T4 payload4,
      T5 payload5, T6 payload6, T7 payload7)
      : DtoPayload<T1, T2, T3, T4, T5, T6>(payload1, payload2, payload3, payload4, payload5, payload6)
   {
      public T7 Payload7 { get; init; } = payload7;
   }

   public class DtoPayload<T1, T2, T3, T4, T5, T6, T7, T8>(T1 payload1, T2 payload2, T3 payload3, T4 payload4,
      T5 payload5, T6 payload6, T7 payload7, T8 payload8)
      : DtoPayload<T1, T2, T3, T4, T5, T6, T7>(payload1, payload2, payload3, payload4, payload5, payload6, payload7)
   {
      public T8 Payload8 { get; init; } = payload8;
   }

   public class DtoPayload<T1, T2, T3, T4, T5, T6, T7, T8, T9>(T1 payload1, T2 payload2, T3 payload3, T4 payload4,
      T5 payload5, T6 payload6, T7 payload7, T8 payload8, T9 payload9)
      : DtoPayload<T1, T2, T3, T4, T5, T6, T7, T8>(payload1, payload2, payload3, payload4, payload5, payload6,
         payload7, payload8)
   {
      public T9 Payload9 { get; init; } = payload9;
   }

   public class DtoPayload<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(T1 payload1, T2 payload2, T3 payload3,
      T4 payload4, T5 payload5, T6 payload6, T7 payload7, T8 payload8, T9 payload9, T10 payload10)
      : DtoPayload<T1, T2, T3, T4, T5, T6, T7, T8, T9>(payload1, payload2, payload3, payload4, payload5, payload6,
         payload7, payload8, payload9)
   {
      public T10 Payload10 { get; init; } = payload10;
   }
}
