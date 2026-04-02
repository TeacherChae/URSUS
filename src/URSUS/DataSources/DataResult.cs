namespace URSUS.DataSources
{
    /// <summary>
    /// 데이터 소스 호출 결과를 담는 봉투(envelope) 타입.
    ///
    /// 성공/실패 여부와 상관없이 항상 이 타입으로 반환하여,
    /// 호출부에서 예외 대신 패턴 매칭으로 에러를 처리할 수 있다.
    /// </summary>
    /// <typeparam name="T">성공 시 반환되는 데이터 타입</typeparam>
    public class DataResult<T>
    {
        /// <summary>성공 여부</summary>
        public bool IsSuccess { get; }

        /// <summary>성공 시 데이터 (실패 시 default)</summary>
        public T? Data { get; }

        /// <summary>실패 시 에러 정보 (성공 시 null)</summary>
        public DataSourceError? Error { get; }

        /// <summary>데이터 출처 (캐시 or API)</summary>
        public DataOrigin Origin { get; }

        /// <summary>호출 소요 시간</summary>
        public TimeSpan Elapsed { get; }

        private DataResult(bool isSuccess, T? data, DataSourceError? error,
                           DataOrigin origin, TimeSpan elapsed)
        {
            IsSuccess = isSuccess;
            Data      = data;
            Error     = error;
            Origin    = origin;
            Elapsed   = elapsed;
        }

        /// <summary>성공 결과 생성</summary>
        public static DataResult<T> Success(T data, DataOrigin origin, TimeSpan elapsed)
            => new(true, data, null, origin, elapsed);

        /// <summary>실패 결과 생성</summary>
        public static DataResult<T> Failure(DataSourceError error, TimeSpan elapsed)
            => new(false, default, error, DataOrigin.None, elapsed);

        /// <summary>성공 시 데이터 변환</summary>
        public DataResult<TOut> Map<TOut>(Func<T, TOut> transform)
        {
            if (!IsSuccess || Data == null)
                return DataResult<TOut>.Failure(Error!, Elapsed);

            return DataResult<TOut>.Success(transform(Data), Origin, Elapsed);
        }
    }

    /// <summary>데이터의 출처</summary>
    public enum DataOrigin
    {
        /// <summary>출처 없음 (에러 시)</summary>
        None,

        /// <summary>로컬 캐시에서 로드</summary>
        Cache,

        /// <summary>API 호출로 수집</summary>
        Api,

        /// <summary>DLL 내장 리소스에서 로드</summary>
        Embedded
    }
}
