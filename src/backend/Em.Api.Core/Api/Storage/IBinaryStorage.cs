namespace Em.Api.Core.Storage
{
   /// <summary>
   /// Storage of file content owned by the application: a file is stored under a key and read back through
   /// that key. There is no public address to the content - whatever fetches it is the consumer's action,
   /// which first checks the caller's rights.
   /// </summary>
   /// <remarks>
   /// Content that has been stored is <b>never overwritten</b>: a key that is already used is refused.
   /// That is deliberate, because its first consumer is frozen files - something a person signed must stay
   /// exactly as they saw it. A new version uses a new key.
   /// <para>
   /// A key is shaped like a path (<c>part/part/name.pdf</c>) so another implementation - network object
   /// storage, for example - can use the same key without changing its consumers. See
   /// <see cref="BinaryStorageKey"/> for the shape rules.
   /// </para>
   /// </remarks>
   public interface IBinaryStorage
   {
      /// <summary>
      /// Stores <paramref name="content"/> under <paramref name="key"/>.
      /// </summary>
      /// <param name="key">The key where the content is stored. See <see cref="BinaryStorageKey"/>.</param>
      /// <param name="content">The content to store, read from its current position to the end.</param>
      /// <param name="cancellationToken">Cancellation token.</param>
      /// <returns>The key that was actually used, already in its canonical form.</returns>
      /// <exception cref="ArgumentException">
      /// Thrown when <paramref name="key"/> does not satisfy the key shape rules.
      /// </exception>
      /// <exception cref="Em.Shared.ActionException">
      /// Thrown when that key is already used - this storage does not overwrite existing content.
      /// </exception>
      Task<string> PutAsync(string key, Stream content, CancellationToken cancellationToken = default);

      /// <summary>
      /// Opens the content stored under a key for reading. The caller closes the stream.
      /// </summary>
      /// <param name="key">Key of the content.</param>
      /// <param name="cancellationToken">Cancellation token.</param>
      /// <exception cref="Em.Shared.ActionException">
      /// Thrown when there is no content under that key.
      /// </exception>
      Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default);

      /// <summary>
      /// Discards the content stored under a key. A key that does not exist is not an error - it is also
      /// called when cleaning up the leftovers of a failed submission, where all that matters is that the
      /// content is gone afterwards.
      /// </summary>
      /// <param name="key">Key of the content.</param>
      /// <param name="cancellationToken">Cancellation token.</param>
      Task DeleteAsync(string key, CancellationToken cancellationToken = default);

      /// <summary>Whether there is content stored under a key.</summary>
      /// <param name="key">The key being checked.</param>
      /// <param name="cancellationToken">Cancellation token.</param>
      Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
   }
}
