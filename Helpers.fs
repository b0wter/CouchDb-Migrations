module Helpers

    module Result =

        /// <summary>
        /// Get a value from a result using a transformation for the Ok and Error case.
        /// </summary>
        let get (transformOk: 'a -> 'c) (transformError: 'b -> 'c) (result: Result<'a,'b>) : 'c =
            match result with
            | Ok r -> transformOk r
            | Error r -> transformError r
