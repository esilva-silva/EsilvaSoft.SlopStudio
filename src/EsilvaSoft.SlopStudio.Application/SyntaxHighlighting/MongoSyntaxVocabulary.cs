using System.Collections.Frozen;

namespace EsilvaSoft.SlopStudio.Application.SyntaxHighlighting;

/// <summary>MongoDB vocabulary shared by visual consumers; never contains user namespace names.</summary>
public static class MongoSyntaxVocabulary
{
    public static FrozenSet<string> Functions { get; } = Set("find findOne aggregate count countDocuments estimatedDocumentCount distinct insertOne insertMany updateOne updateMany replaceOne deleteOne deleteMany bulkWrite createIndex createIndexes dropIndex dropIndexes getIndexes getIndexSpecs findOneAndUpdate findOneAndReplace findOneAndDelete renameCollection drop stats explain limit skip sort project hint collation maxTimeMS batchSize toArray forEach map getCollection getSiblingDB getDatabase getDB getCollectionNames getName print printjson show use help");
    public static FrozenSet<string> Operators { get; } = Set("$eq $ne $gt $gte $lt $lte $in $nin $and $or $nor $not $exists $type $regex $options $expr $elemMatch $size $all $sum $avg $min $max $push $addToSet $first $last $cond $ifNull $set $unset $inc $mul $rename $setOnInsert $pull $pullAll $pop $each $slice $position $currentDate $jsonSchema $text $where");
    public static FrozenSet<string> AggregationStages { get; } = Set("$match $group $project $sort $limit $skip $lookup $unwind $facet $count $set $unset $addFields $replaceRoot $replaceWith $bucket $bucketAuto $merge $out $search $searchMeta $unionWith $sample $sortByCount $geoNear $setWindowFields $densify $fill $documents $graphLookup");
    public static FrozenSet<string> AtlasSearchOperators { get; } = Set("compound must mustNot should filter text autocomplete equals range near phrase regex wildcard exists embeddedDocument moreLikeThis queryString path query value index score minimumShouldMatch");
    public static FrozenSet<string> ExtendedJsonTypes { get; } = Set("ObjectId ISODate NumberLong NumberInt NumberDecimal UUID CGUUID JUUID GUUID BinData Timestamp MinKey MaxKey Date RegExp Decimal128 Long Int32 Double Binary $oid $date $numberLong $numberInt $numberDecimal $numberDouble $binary $timestamp $minKey $maxKey $regularExpression $undefined $code $scope $dbPointer $symbol");
    public static FrozenSet<string> Keywords { get; } = Set("const let var async await if else for while do return try catch finally throw function new break continue switch case default of in this typeof instanceof void delete yield class extends true false null undefined");
    public static FrozenSet<string> DslFunctions { get; } = Set("getConnection getDatabase GetDatabase getCollection GetCollection");
    public static FrozenSet<string> DslRoots { get; } = Set("ConnectionPool ConnectionPull");
    private static FrozenSet<string> Set(string words) => words.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToFrozenSet(StringComparer.Ordinal);
}
